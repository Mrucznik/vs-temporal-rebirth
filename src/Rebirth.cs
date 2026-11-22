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
internal class Rebirth(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Messages messages)
{
    /// <summary>Synced, so clients can show what a player has become.</summary>
    internal const string FormAttr = "temporalrebirthForm";

    /// <summary>The vanilla death screen opens 2.5 s after death; players are turned into something before that.</summary>
    private const int SettleDelayMs = 1500;

    private const string OldReturningKey = "temporalrebirth.returning";

    private readonly Dictionary<string, long> lastTetherMessageMs = new();
    private readonly Dictionary<string, Vec3d> lastInside = new();

    /// <summary>The latest teleport of each player; older ones that finish late are ignored.</summary>
    private readonly Dictionary<string, int> journeys = new();

    public Afterlife Afterlife => afterlife;

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

        LostPlayer? lost = afterlife.Get(player.PlayerUID);
        if (lost == null)
        {
            player.Entity.WatchedAttributes.RemoveAttribute(FormAttr);
            if (player.GetModData(OldReturningKey, false))
            {
                // 0.6.x: left mid-return, so the switch back to survival never happened.
                player.SetModData(OldReturningKey, false);
                SetGameMode(player, EnumGameMode.Survival);
            }

            // Died on their last life without the mod noticing: they would be stuck on the death screen.
            if (player.Entity is { Alive: false } && !Unlimited && LifeRules.GetLivesLeft(player) == 0)
            {
                Lose(player);
            }

            return;
        }

        if (lost.Phase == Phase.Returning)
        {
            Travel(player, lost);
            return;
        }

        CheckReturn(player.PlayerUID, lost);
        if (lost.Phase != Phase.Returning)
        {
            Enter(player, lost);
            TellWhenReturn(player, lost);
        }
    }

    public void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        // Vanilla increments WorldData.Deaths before raising PlayerDeath.
        LifeRules.Sync(player);
        if (Unlimited)
        {
            return;
        }

        if (afterlife.Contains(player.PlayerUID))
        {
            // Dying while out of lives costs nothing and sets nobody free.
            SettleLater(player.PlayerUID);
        }
        else if (LifeRules.GetLivesLeft(player) == 0)
        {
            Lose(player);
        }
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

        entity.WatchedAttributes.SetString(FormAttr, rules.Form == Form.Zombie && !rules.ZombieGlow ? "Player" : rules.Form.ToString());
        Vec3d start = lost.StartVec ?? lost.DeathVec ?? entity.Pos.XYZ;
        lost.Start ??= LostPlayer.Pack(start);

        if (!Walks(rules.Form))
        {
            SetGameMode(player, EnumGameMode.Spectator);
            if (first || lost.Travelling)
            {
                lost.Travelling = false;
                if (entity.Pos.XYZ.DistanceTo(start) > 0.1)
                {
                    entity.TeleportToDouble(start.X, start.Y, start.Z);
                }
            }

            return;
        }

        if (!first && !died && !lost.Travelling)
        {
            return;
        }

        if (entity.Pos.XYZ.DistanceTo(start) < 1)
        {
            Land(player.PlayerUID);
            return;
        }

        // Travel as a spectator and become mortal only once the chunks are there, so nobody falls into unloaded terrain.
        lost.Travelling = true;
        SetGameMode(player, EnumGameMode.Spectator);
        GoTo(player, start, () => Land(player.PlayerUID));
    }

    private void Land(string uid)
    {
        if (Online(uid) is not { } player || afterlife.Get(uid) is not { Phase: Phase.Serving } lost)
        {
            return;
        }

        lost.Travelling = false;
        SetGameMode(player, EnumGameMode.Survival);
    }

    private Vec3d StartOf(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        Vec3d death = lost.DeathVec ?? player.Entity.Pos.XYZ;
        switch (rules.PlaceAt)
        {
            case Place.Spawn:
                return player.GetSpawnPosition(false).XYZ;
            case Place.Fixed:
            case Place.Zone:
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

    // ---------- tether ----------

    /// <summary>Called every second.</summary>
    public void EnforceTethers()
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
            if (rules.Radius > 0 && lost.StartVec is { } anchor && player.Entity.Alive)
            {
                Tether(player, anchor, rules.Radius, Walks(rules.Form));
            }
        }
    }

    private void Tether(IServerPlayer player, Vec3d anchor, double radius, bool walks)
    {
        Vec3d pos = player.Entity.Pos.XYZ;
        double dist = pos.DistanceTo(anchor);
        if (dist <= radius)
        {
            lastInside[player.PlayerUID] = pos;
            return;
        }

        Vec3d target;
        if (walks)
        {
            // Back to the last step inside the limit.
            target = lastInside.TryGetValue(player.PlayerUID, out Vec3d? inside) && inside.DistanceTo(anchor) <= radius ? inside : anchor;
        }
        else
        {
            // Teleports far away (e.g. an admin /tp) snap straight back; drifting ends on the edge, still facing where it was heading.
            target = dist > radius * 4
                ? anchor
                : anchor.AddCopy((pos.X - anchor.X) / dist * (radius - 1), (pos.Y - anchor.Y) / dist * (radius - 1), (pos.Z - anchor.Z) / dist * (radius - 1));
            if (!player.WorldData.NoClip && !SafeSpot.IsFree(api.World.BlockAccessor, target))
            {
                // The line can cross a hill; a solid ghost must not land inside it.
                target = SafeSpot.FindAbove(api.World.BlockAccessor, target) ?? SafeSpot.FindAbove(api.World.BlockAccessor, anchor) ?? anchor;
            }
        }

        player.Entity.TeleportToDouble(target.X, target.Y, target.Z);

        long now = api.World.ElapsedMilliseconds;
        if (!lastTetherMessageMs.TryGetValue(player.PlayerUID, out long last) || now - last > 15_000)
        {
            lastTetherMessageMs[player.PlayerUID] = now;
            messages.Tell(player, "TooFar");
        }
    }

    // ---------- coming back ----------

    /// <summary>Called periodically.</summary>
    public void CheckReturns()
    {
        foreach (var (uid, lost) in afterlife.Players.ToList())
        {
            if (Online(uid) != null)
            {
                CheckReturn(uid, lost);
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

    /// <summary>Back by a ritual to <paramref name="destination"/>; null means wherever the player is.</summary>
    public void ReturnByRitual(string uid, LostPlayer lost, string helper, Vec3d? destination)
    {
        lost.Helper = helper;
        lost.Destination = destination == null ? null : LostPlayer.Pack(destination);
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
        if (!entity.Alive)
        {
            entity.Revive();
            DownedCompat.ClearDeathFlags(entity);
        }

        // Same order as vanilla respawn: wait for the chunks at the destination and only then become mortal.
        SetGameMode(player, EnumGameMode.Spectator);
        string uid = player.PlayerUID;
        GoTo(player, DestinationOf(player, lost), () => Arrive(uid));
    }

    private Vec3d DestinationOf(IServerPlayer player, LostPlayer lost)
    {
        if (lost.Reason == ReturnReason.Ritual)
        {
            Vec3d here = lost.DestinationVec ?? player.Entity.Pos.XYZ;
            return lost.DestinationVec ?? SafeSpot.FindGround(api.World.BlockAccessor, here) ?? here;
        }

        Vec3d spawn = player.GetSpawnPosition(false).XYZ;
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
        lastInside.Remove(uid);
        entity.WatchedAttributes.RemoveAttribute(FormAttr);
        entity.WatchedAttributes.MarkPathDirty(FormAttr);
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

        api.ChatCommands.ExecuteUnparsed($"/player {player.PlayerName} gamemode {modeName}", args, result =>
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
}
