using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace TemporalRebirth;

/// <summary>
/// Server-side lifecycle of a player who runs out of lives: Dying -> Serving -> Returning -> gone from the registry.
///
/// Everything delayed (the 1.5 s before the death screen, teleports waiting for chunks) goes by player uid and the
/// saved phase, never by a captured player object: the player may have left, come back as a new object, or the server
/// may have restarted in between.
/// </summary>
internal class Rebirth(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Zones zones, Messages messages)
{
    /// <summary>The vanilla death screen opens 2.5 s after death; players are turned into something before that.</summary>
    private const int SettleDelayMs = 1500;

    /// <summary>Dying again this soon after being let loose moves a player from where they died to their spawn point.</summary>
    private const int QuickDeathSeconds = 60;

    /// <summary>How long nobody can hurt a walking player who was just let loose.</summary>
    private const int ProtectedSeconds = 5;

    private const string OldReturningKey = "temporalrebirth.returning";

    private readonly Leash leash = new(api, config, zones, messages);
    private Downs? downs;

    /// <summary>The latest teleport of each player; older ones that finish late are ignored.</summary>
    private readonly Dictionary<string, int> journeys = new();

    public Afterlife Afterlife => afterlife;

    private Downs Downs => downs ??= new Downs(api, config, afterlife, OnLastDown);

    public RebirthConfig Rules(LostPlayer lost) => config.With(lost.Settings);

    public Form FormOf(LostPlayer lost) => Rules(lost).Form;

    public static bool Walks(Form form) => form is Form.Zombie or Form.Player;

    private bool Unlimited => LifeRules.UnlimitedLives(api.World);

    /// <summary>An online player with an entity, at any point after joining.</summary>
    private IServerPlayer? Online(string uid) =>
        api.World.PlayerByUid(uid) is IServerPlayer { Entity: not null, ConnectionState: EnumClientState.Connected or EnumClientState.Playing } player ? player : null;

    // ---------- events ----------

    public void OnPlayerNowPlaying(IServerPlayer player)
    {
        LifeRules.EnsureBaseline(player);
        LifeRules.Sync(player);
        LifeRules.SetMayRespawn(player.Entity, !player.Entity.Alive && LifeRules.MayRespawn(player.Entity));
        Downs.Watch(player);

        LostPlayer? lost = afterlife.Get(player.PlayerUID);
        if (lost == null)
        {
            ClientState.Clear(player.Entity);
            if (player.GetModData(OldReturningKey, false))
            {
                // 0.6.x: left mid-return, so the switch back to survival never happened.
                player.SetModData(OldReturningKey, false);
                SetGameMode(player, EnumGameMode.Survival);
            }

            // Died on their last life without the mod noticing: they would be stuck on the death screen.
            // With DeadNotify they died while joining and the game is about to count it.
            if (player.Entity is { Alive: false, DeadNotify: false } && !Unlimited && LifeRules.GetLivesLeft(player) == 0 && !LifeRules.MayRespawn(player.Entity))
            {
                // Unless they left dying of a down, before the game counted the death.
                if (Downs.TakePaid(player) && Downs.Mode != LivesCount.DownsFinalDeath)
                {
                    LifeRules.SetMayRespawn(player.Entity, true);
                }
                else
                {
                    Lose(player);
                }
            }

            return;
        }

        if (lost.Phase == Phase.Returning)
        {
            Travel(player, lost);
            return;
        }

        // Becoming something says when they return by itself.
        bool dying = lost.Phase == Phase.Dying;
        CheckReturn(player.PlayerUID, lost);
        if (lost.Phase != Phase.Returning)
        {
            Enter(player, lost);
            if (!dying)
            {
                TellWhenReturn(player, lost);
            }
        }
    }

    public void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        // Vanilla increments WorldData.Deaths before raising PlayerDeath.
        LifeRules.SetMayRespawn(player.Entity, false);
        LifeRules.Sync(player);
        if (Unlimited)
        {
            return;
        }

        if (afterlife.Contains(player.PlayerUID))
        {
            // Dying while out of lives costs nothing and sets nobody free.
            Downs.TakePaid(player);
            SettleLater(player.PlayerUID);
        }
        else if (Downs.ChargeDeath(player))
        {
            Lose(player);
        }
        else if (LifeRules.GetLivesLeft(player) == 0)
        {
            LifeRules.SetMayRespawn(player.Entity, true);
        }
    }

    public void OnPlayerRespawn(IServerPlayer player) => LifeRules.SetMayRespawn(player.Entity, false);

    /// <summary>Downed with no lives left: nobody gets up from this one.</summary>
    private void OnLastDown(IServerPlayer player)
    {
        Lose(player);
        player.Entity.Die(EnumDespawnReason.Death, DownedCompat.CauseOfDown(player.Entity));
    }

    private void Lose(IServerPlayer player)
    {
        afterlife.Add(player.PlayerUID, player.PlayerName, player.Entity.Pos.XYZ, config.Snapshot());
        SettleLater(player.PlayerUID);
    }

    private void SettleLater(string uid) => api.World.RegisterCallback(_ => Settle(uid), SettleDelayMs);

    private void Settle(string uid)
    {
        if (Online(uid) is not { } player || afterlife.Get(uid) is not { } lost)
        {
            return;
        }

        if (lost.Phase == Phase.Returning)
        {
            Travel(player, lost);
        }
        else
        {
            Enter(player, lost);
        }
    }

    // ---------- out of lives ----------

    /// <summary>Makes the player what their settings say. Safe to repeat: after a relog, or after dying as a zombie.</summary>
    private void Enter(IServerPlayer player, LostPlayer lost)
    {
        RebirthConfig rules = Rules(lost);
        EntityPlayer entity = player.Entity;
        bool first = lost.Phase == Phase.Dying;
        if (first)
        {
            lost.Phase = Phase.Serving;
            lost.Start = LostPlayer.Pack(StartOf(player, lost, rules));
            lost.Travelling = true;
            afterlife.Save();
            Announce(player, lost, rules);
        }

        // Someone left sitting on a mount or in a boat could keep steering it.
        entity.TryUnmount();
        bool died = !entity.Alive;
        if (died)
        {
            entity.Revive();
            DownedCompat.ClearDeathFlags(entity);
        }

        if (rules.Form == Form.Ban)
        {
            Kick(player.PlayerUID, lost);
            return;
        }

        ClientState.Set(entity, ClientState.Form, rules.Form == Form.Zombie && !rules.ZombieGlow ? "Player" : rules.Form.ToString());
        ClientState.Set(entity, ClientState.GhostLook, rules.Form == Form.Ghost && rules.GhostVisible != GhostVisible.Nobody ? rules.GhostVisible.ToString() : null);
        SyncTimer(player, lost);
        if (died && !first && rules.PlaceAt == Place.Body && Walks(rules.Form) && lost.LandedAtUtc is { } landed && DateTime.UtcNow < landed.AddSeconds(QuickDeathSeconds))
        {
            MoveToSpawn(player, lost);
        }

        Vec3d start = lost.StartVec ?? lost.DeathVec ?? entity.Pos.XYZ;
        lost.Start ??= LostPlayer.Pack(start);

        if (!Walks(rules.Form))
        {
            SetGameMode(player, EnumGameMode.Spectator);
            died = false;
        }

        if (!first && !died && !lost.Travelling)
        {
            return;
        }

        if (entity.Pos.XYZ.DistanceTo(start) < (Walks(rules.Form) ? 1 : 0.1) && SafeSpot.IsLoaded(api.World.BlockAccessor, start))
        {
            Land(player.PlayerUID);
            return;
        }

        // Travel as a spectator and become mortal only once the chunks are there, so nobody falls into unloaded terrain.
        lost.Travelling = true;
        ClientState.Set(entity, ClientState.Leash, (string?)null);
        SetGameMode(player, EnumGameMode.Spectator);
        GoTo(player, start, () => Land(player.PlayerUID));
    }

    private void Land(string uid)
    {
        if (Online(uid) is not { } player || afterlife.Get(uid) is not { Phase: Phase.Serving } lost)
        {
            return;
        }

        RebirthConfig rules = Rules(lost);
        if (!Walks(rules.Form))
        {
            lost.Travelling = false;
            return;
        }

        // Only now the land is loaded for sure.
        Vec3d here = player.Entity.Pos.XYZ;
        Vec3d? ground = here;
        if (rules.PlaceAt == Place.Body)
        {
            ground = SafeSpot.FindGround(api.World.BlockAccessor, here);
            if (ground == null && !lost.AtSpawn)
            {
                // Died at sea or in a lake of lava.
                MoveToSpawn(player, lost);
                lost.Travelling = true;
                SetGameMode(player, EnumGameMode.Spectator);
                GoTo(player, lost.StartVec!, () => Land(uid));
                return;
            }
        }
        else if (rules.PlaceAt == Place.Zone && zones.Claims(rules.PlaceGroup) is { Count: > 0 } claims)
        {
            ground = zones.Ground(claims, here);
        }

        if (ground != null && ground.DistanceTo(here) > 1)
        {
            player.Entity.TeleportToDouble(ground.X, ground.Y, ground.Z);
            lost.Start = LostPlayer.Pack(ground);
        }

        lost.Travelling = false;
        lost.LandedAtUtc = DateTime.UtcNow;
        SetGameMode(player, EnumGameMode.Survival);
    }

    private static void MoveToSpawn(IServerPlayer player, LostPlayer lost)
    {
        lost.AtSpawn = true;
        lost.Start = LostPlayer.Pack(player.GetSpawnPosition(false).XYZ);
    }

    private Vec3d StartOf(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        Vec3d death = lost.DeathVec ?? player.Entity.Pos.XYZ;
        switch (rules.PlaceAt)
        {
            case Place.Spawn:
                return player.GetSpawnPosition(false).XYZ;
            case Place.Zone:
                if (StartInZone(player, death, rules) is { } inZone)
                {
                    return inZone;
                }

                break;
            case Place.Fixed:
                if (afterlife.Point("start") is { } point)
                {
                    return point;
                }

                api.Logger.Error("[temporalrebirth] No place is marked for players out of lives (/rebirth point set start); {0} stays where they died.", player.PlayerName);
                break;
        }

        // Died inside a block (suffocation, sand): a solid ghost would be stuck there.
        return rules.Form == Form.Ghost ? SafeSpot.FindAbove(api.World.BlockAccessor, death) ?? death : death;
    }

    /// <summary>The marked place if it is in the zone, else where they died if that is, else the closest spot of the zone.</summary>
    private Vec3d? StartInZone(IServerPlayer player, Vec3d death, RebirthConfig rules)
    {
        Vec3d? point = afterlife.Point("start");
        List<LandClaim> claims = zones.Claims(rules.PlaceGroup);
        if (claims.Count == 0)
        {
            leash.ReportMissingZone(player, rules.PlaceGroup);
            return point;
        }

        if (point != null && Zones.Contains(claims, point))
        {
            return point;
        }

        api.Logger.Error("[temporalrebirth] No place is marked inside the zone of the group {0} (/rebirth point set start).", rules.PlaceGroup);
        return Zones.Contains(claims, death) ? null : zones.Nearest(claims, point ?? death);
    }

    private void Announce(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        if (rules.Form != Form.Ban)
        {
            messages.Tell(player, rules.Form.ToString(), ("player", player.PlayerName));
            TellWhenReturn(player, lost);
        }

        messages.TellEveryone("AnnounceOut", ("player", player.PlayerName));
    }

    private void TellWhenReturn(IServerPlayer player, LostPlayer lost)
    {
        if (FormOf(lost) == Form.Ban)
        {
            return;
        }

        if (ReturnAtUtc(lost) is { } at)
        {
            messages.Tell(player, "ReturnAt", ("time", Messages.Time(at)));
        }
        else
        {
            messages.Tell(player, "NoReturn");
        }
    }

    // ---------- ban ----------

    /// <summary>What a player trying to join is told, or null when they may join.</summary>
    public string? RefuseJoin(string? uid)
    {
        if (uid == null || Unlimited || afterlife.Get(uid) is not { } lost || lost.Phase == Phase.Returning || FormOf(lost) != Form.Ban)
        {
            return null;
        }

        // Dying: not turned into anything yet, but the ban already counts.
        return ReturnAtUtc(lost) is { } at && DateTime.UtcNow >= at ? null : BanText(null, lost);
    }

    private string BanText(string? language, LostPlayer lost)
    {
        if (ReturnAtUtc(lost) is { } at)
        {
            return messages.Text(language, "Ban", ("time", Messages.Time(at)), ("player", lost.Name));
        }

        // Without an end there is no time to fill in, unless the admin wrote a text of their own.
        return config.Message("Ban") != null ? messages.Text(language, "Ban", ("time", "-"), ("player", lost.Name)) : Lang.GetL(language ?? Lang.CurrentLocale, "temporalrebirth:ban-forever");
    }

    private void Kick(string uid, LostPlayer lost)
    {
        // IServerPlayer.Disconnect(message) only writes the message to the log (1.22.7); this overload shows it.
        var server = (ServerMain)api.World;
        ConnectedClient? client = server.Clients.Values.FirstOrDefault(c => c.Player?.PlayerUID == uid);
        if (client != null)
        {
            server.DisconnectPlayer(client, null, BanText(client.Player.LanguageCode, lost));
        }
    }

    // ---------- staying in place ----------

    /// <summary>Called every second.</summary>
    public void EnforceLimits()
    {
        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            if (player.Entity == null || player.ConnectionState != EnumClientState.Playing
                || afterlife.Get(player.PlayerUID) is not { Phase: Phase.Serving, Travelling: false } lost)
            {
                continue;
            }

            RebirthConfig rules = Rules(lost);
            KeepNoClipSetting(player, rules);
            if (!leash.Enforce(player, lost, rules))
            {
                lost.Travelling = true;
                Enter(player, lost);
            }
        }
    }

    /// <summary>Whether a walking player was let loose so recently that nothing may hurt them.</summary>
    public bool IsProtected(string uid) =>
        afterlife.Get(uid) is { Phase: Phase.Serving, LandedAtUtc: { } landed } && DateTime.UtcNow < landed.AddSeconds(ProtectedSeconds);

    // ---------- coming back ----------

    /// <summary>Called periodically.</summary>
    public void CheckReturns()
    {
        foreach (var (uid, lost) in afterlife.Players.ToList())
        {
            if (Online(uid) != null)
            {
                CheckReturn(uid, lost);
                if (lost.Phase == Phase.Serving && Online(uid) is { } player)
                {
                    SyncTimer(player, lost);
                }
            }
        }
    }

    private void CheckReturn(string uid, LostPlayer lost)
    {
        if (lost.Phase == Phase.Returning)
        {
            return;
        }

        // The world went back to unlimited lives (or the mod disabled itself): nobody should stay out.
        if (Unlimited)
        {
            BeginReturn(uid, lost, ReturnReason.Released);
        }
        else if (ReturnAtUtc(lost) is { } at && DateTime.UtcNow >= at)
        {
            BeginReturn(uid, lost, ReturnReason.Time);
        }
    }

    public DateTime? ReturnAtUtc(LostPlayer lost) => Rules(lost).Duration.EndUtc(lost.DiedAtUtc);

    /// <summary>Back by the settings' "where they come back", now or at the next login.</summary>
    public void ReturnByAdmin(string uid, LostPlayer lost, int? lives = null)
    {
        lost.Lives = lives;
        BeginReturn(uid, lost, ReturnReason.Admin);
    }

    public void ReturnByRitual(string uid, LostPlayer lost, IServerPlayer caster, ReviveAt at)
    {
        lost.Helper = caster.PlayerName;
        lost.Destination = at switch
        {
            ReviveAt.Caster => LostPlayer.Pack(caster.Entity.Pos.XYZ),
            ReviveAt.Body => lost.DeathPos,
            _ => null,
        };
        lost.ToSpawn = at == ReviveAt.Spawn || (at == ReviveAt.Body && lost.DeathPos == null);
        BeginReturn(uid, lost, ReturnReason.Ritual);
    }

    private void BeginReturn(string uid, LostPlayer lost, ReturnReason reason)
    {
        if (lost.Phase == Phase.Returning)
        {
            return;
        }

        bool entered = lost.Phase == Phase.Serving;
        lost.Phase = Phase.Returning;
        lost.Reason = reason;
        afterlife.Save();

        if (Online(uid) is not { } player)
        {
            return;
        }

        RebirthConfig rules = Rules(lost);
        if (reason == ReturnReason.Time && entered && rules.DieAtEnd && Walks(rules.Form) && player.Entity.Alive)
        {
            // Belongings drop by the world's rules; OnPlayerDeath then continues the journey.
            player.Entity.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Injury });
            return;
        }

        Travel(player, lost);
    }

    private void Travel(IServerPlayer player, LostPlayer lost)
    {
        EntityPlayer entity = player.Entity;
        entity.TryUnmount();
        ClientState.Clear(entity);
        if (!entity.Alive)
        {
            entity.Revive();
            DownedCompat.ClearDeathFlags(entity);
        }
        else if (DownedCompat.IsDowned(entity))
        {
            // Went down while out of lives; that must not cost the life they come back with.
            RunForPlayer(player, $"/downed revive {player.PlayerName}", _ => { });
        }

        // Same order as vanilla respawn: wait for the chunks at the destination and only then become mortal.
        SetGameMode(player, EnumGameMode.Spectator);
        string uid = player.PlayerUID;
        GoTo(player, DestinationOf(player, lost), () => Arrive(uid));
    }

    private Vec3d DestinationOf(IServerPlayer player, LostPlayer lost)
    {
        if (lost.Reason == ReturnReason.Ritual && !lost.ToSpawn)
        {
            Vec3d here = player.Entity.Pos.XYZ;
            IBlockAccessor blocks = api.World.BlockAccessor;
            return lost.DestinationVec ?? SafeSpot.FindGround(blocks, here) ?? SafeSpot.FindGround(blocks, here, true) ?? here;
        }

        Vec3d spawn = player.GetSpawnPosition(false).XYZ;
        if (lost.Reason == ReturnReason.Ritual)
        {
            return spawn;
        }

        switch (Rules(lost).ReturnAt)
        {
            case Place.Body:
                return lost.DeathVec ?? spawn;
            case Place.Fixed:
                if (afterlife.Point("return") is { } point)
                {
                    return point;
                }

                api.Logger.Error("[temporalrebirth] No place is marked for coming back (/rebirth point set return); {0} goes to their spawn point.", player.PlayerName);
                return spawn;
            default:
                return spawn;
        }
    }

    private void Arrive(string uid)
    {
        if (Online(uid) is not { } player || afterlife.Get(uid) is not { Phase: Phase.Returning } lost)
        {
            return;
        }

        RebirthConfig rules = Rules(lost);
        EntityPlayer entity = player.Entity;
        if (!entity.Alive)
        {
            entity.Revive();
            DownedCompat.ClearDeathFlags(entity);
        }

        if (!Unlimited)
        {
            int lives = lost.Lives ?? rules.ReturnLives;
            LifeRules.SetLivesLeft(player, lives < 0 ? Math.Max(1, LifeRules.WorldLives(api.World)) : lives);
        }

        afterlife.Remove(uid);
        leash.Forget(uid);
        ClientState.Clear(entity);
        LifeRules.Sync(player);
        SetGameMode(player, EnumGameMode.Survival);
        SetVital(entity, "health", "currenthealth", "maxhealth", rules.HealthPercent, 1f);
        SetVital(entity, "hunger", "currentsaturation", "maxsaturation", rules.SatietyPercent, 0f);

        switch (lost.Reason)
        {
            case ReturnReason.Ritual:
                messages.Tell(player, "RevivedRitual", ("caster", lost.Helper ?? ""), ("player", player.PlayerName));
                break;
            case ReturnReason.Admin:
                messages.Tell(player, "RevivedAdmin", ("player", player.PlayerName));
                messages.TellEveryone("AnnounceReturned", ("player", player.PlayerName));
                break;
            default:
                messages.Tell(player, "Returned", ("player", player.PlayerName));
                messages.TellEveryone("AnnounceReturned", ("player", player.PlayerName));
                break;
        }
    }

    /// <summary>Sets a vital to an exact percentage of its maximum; -1 leaves what the game gave.</summary>
    private static void SetVital(EntityPlayer entity, string tree, string currentKey, string maxKey, int percent, float minimum)
    {
        if (percent < 0 || entity.WatchedAttributes.GetTreeAttribute(tree) is not { } attrs)
        {
            return;
        }

        float max = attrs.GetFloat(maxKey, 0f);
        if (max > 0)
        {
            attrs.SetFloat(currentKey, Math.Max(minimum, max * percent / 100f));
            entity.WatchedAttributes.MarkPathDirty(tree);
        }
    }

    // ---------- helpers ----------

    private void GoTo(IServerPlayer player, Vec3d pos, Action arrived)
    {
        string uid = player.PlayerUID;
        int journey = journeys[uid] = journeys.GetValueOrDefault(uid) + 1;
        EntityPos target = player.Entity.Pos.Copy();
        // Keeps the dimension of the target, not of where the player happens to be.
        target.SetPosWithDimension(pos);
        player.Entity.TeleportTo(target, () =>
        {
            if (journeys.GetValueOrDefault(uid) == journey)
            {
                arrived();
            }
        });
    }

    /// <summary>
    /// Ghosts collide with blocks, spectators do not. Spectator mode turns no-clip on and the client can toggle it,
    /// so this also runs on every tether tick.
    /// </summary>
    private void KeepNoClipSetting(IServerPlayer player, RebirthConfig rules)
    {
        bool noClip = rules.Form == Form.Spectator;
        if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator && player.WorldData.NoClip != noClip)
        {
            player.WorldData.NoClip = noClip;
            player.BroadcastPlayerData(false);
        }
    }

    /// <summary>
    /// Goes through the vanilla command so every server system sees the change like an admin switch.
    /// The caller is the player themself: vanilla then skips its "Console Admin has set your gamemode"
    /// chat line, which is not translated on the server and would clash with our localized messages.
    /// </summary>
    private void SetGameMode(IServerPlayer player, EnumGameMode mode)
    {
        if (player.WorldData.CurrentGameMode == mode)
        {
            return;
        }

        string modeName = mode == EnumGameMode.Spectator ? "spectator" : "survival";
        RunForPlayer(player, $"/player {player.PlayerName} gamemode {modeName}", result =>
        {
            if (result.Status != EnumCommandStatus.Success)
            {
                api.Logger.Error("[temporalrebirth] Could not set {0} to {1}: {2}", player.PlayerName, modeName, result.StatusMessage);
            }
            else if (mode == EnumGameMode.Spectator && afterlife.Get(player.PlayerUID) is { } lost)
            {
                KeepNoClipSetting(player, Rules(lost));
            }
        });
    }

    private void RunForPlayer(IServerPlayer player, string command, Action<TextCommandResult> done)
    {
        var args = new TextCommandCallingArgs
        {
            Caller = new Caller
            {
                Type = EnumCallerType.Console,
                CallerRole = "admin",
                CallerPrivileges = ["*"],
                FromChatGroupId = GlobalConstants.ConsoleGroup,
                Player = player,
            },
        };

        api.ChatCommands.ExecuteUnparsed(command, args, result => done(result));
    }

    /// <summary>How long until they come back, for the timer on their screen.</summary>
    private void SyncTimer(IServerPlayer player, LostPlayer lost)
    {
        RebirthConfig rules = Rules(lost);
        int? seconds = rules.ShowTimer && rules.Form != Form.Ban && ReturnAtUtc(lost) is { } at
            ? (int)Math.Clamp((at - DateTime.UtcNow).TotalSeconds, 0, int.MaxValue)
            : null;
        ClientState.Set(player.Entity, ClientState.TimerSeconds, seconds);
        ClientState.Set(player.Entity, ClientState.TimerText, seconds == null ? null : config.Message("Timer"));
    }
}
