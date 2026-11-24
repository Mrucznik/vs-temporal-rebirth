using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Server-side lifecycle of a player who runs out of lives: Dying -> Serving -> Returning (see <see cref="Returns"/>) -> gone
/// from the registry.
///
/// Everything delayed (the 1.5 s before the death screen, teleports waiting for chunks) goes by player uid and the
/// saved phase, never by a captured player object: the player may have left, come back as a new object, or the server
/// may have restarted in between.
/// </summary>
internal class Rebirth(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, PlayerControl control, Places places, Leash leash, Bans bans, Returns returns, Messages messages)
{
    /// <summary>The vanilla death screen opens 2.5 s after death; players are turned into something before that.</summary>
    private const int SettleDelayMs = 1500;

    /// <summary>Dying again this soon after being let loose moves a player from where they died to their spawn point.</summary>
    private const int QuickDeathSeconds = 60;

    /// <summary>How long nobody can hurt a walking player who was just let loose.</summary>
    private const int ProtectedSeconds = 5;

    private const string OldReturningKey = "temporalrebirth.returning";

    private Downs? downs;

    private Downs Downs => downs ??= new Downs(api, config, afterlife, OnLastDown);

    private bool Unlimited => LifeRules.UnlimitedLives(api.World);

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
                control.MakeMortal(player);
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
            returns.Travel(player, lost);
            return;
        }

        // Becoming something says when they return by itself.
        bool dying = lost.Phase == Phase.Dying;
        returns.Check(player.PlayerUID, lost);
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
        if (control.Online(uid) is not { } player || afterlife.Get(uid) is not { } lost)
        {
            return;
        }

        if (lost.Phase == Phase.Returning)
        {
            returns.Travel(player, lost);
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
        RebirthConfig rules = afterlife.Rules(lost);
        bool first = lost.Phase == Phase.Dying;
        if (first)
        {
            BeginServing(player, lost, rules);
        }

        // Someone left sitting on a mount or in a boat could keep steering it.
        player.Entity.TryUnmount();
        bool died = PlayerControl.Revive(player.Entity);

        if (rules.Form == Form.Ban)
        {
            bans.Kick(player.PlayerUID, lost);
            return;
        }

        ShowForm(player, lost, rules);
        if (died && !first && DiedRightAfterLanding(lost, rules))
        {
            MoveToSpawn(player, lost);
        }

        lost.Start ??= LostPlayer.Pack(lost.DeathVec ?? player.Entity.Pos.XYZ);

        if (!rules.Walks)
        {
            // For flying forms a death is no reason to move them.
            control.MakeSpectator(player, rules.Form == Form.Spectator);
            died = false;
        }

        // A relog of someone already let loose leaves them where they are.
        if (first || died || lost.Travelling)
        {
            GoToStart(player, lost, rules);
        }
    }

    /// <summary>The death was counted: where they stay is decided once, and everyone is told.</summary>
    private void BeginServing(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        lost.Phase = Phase.Serving;
        lost.Start = LostPlayer.Pack(places.StartOf(player, lost, rules));
        lost.Travelling = true;
        afterlife.Save();
        Announce(player, lost, rules);
    }

    /// <summary>What clients draw for this player, and their timer.</summary>
    private void ShowForm(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        ClientState.Set(player.Entity, ClientState.Form, rules.Form == Form.Zombie && !rules.ZombieGlow ? "Player" : rules.Form.ToString());
        ClientState.Set(player.Entity, ClientState.GhostLook, rules.Form == Form.Ghost && rules.GhostVisible != GhostVisible.Nobody ? rules.GhostVisible.ToString() : null);
        SyncTimer(player, lost);
    }

    /// <summary>A walking player who died again right after rising by their body: that place is deadly.</summary>
    private static bool DiedRightAfterLanding(LostPlayer lost, RebirthConfig rules) =>
        rules.PlaceAt == Place.Body && rules.Walks && lost.LandedAtUtc is { } landed && DateTime.UtcNow < landed.AddSeconds(QuickDeathSeconds);

    /// <summary>Lets them loose where they are when that is the start already, otherwise takes them there first.</summary>
    private void GoToStart(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        Vec3d start = lost.StartVec!;
        if (player.Entity.Pos.XYZ.DistanceTo(start) < (rules.Walks ? 1 : 0.1) && SafeSpot.IsLoaded(api.World.BlockAccessor, start))
        {
            Land(player.PlayerUID);
            return;
        }

        TravelToStart(player, lost, rules);
    }

    /// <summary>As a spectator, mortal only once the chunks are there, so nobody falls into unloaded terrain.</summary>
    private void TravelToStart(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        lost.Travelling = true;
        ClientState.Set(player.Entity, ClientState.Leash, (string?)null);
        control.MakeSpectator(player, rules.Form == Form.Spectator);
        string uid = player.PlayerUID;
        control.GoTo(player, lost.StartVec!, () => Land(uid));
    }

    private void Land(string uid)
    {
        if (control.Online(uid) is not { } player || afterlife.Get(uid) is not { Phase: Phase.Serving } lost)
        {
            return;
        }

        RebirthConfig rules = afterlife.Rules(lost);
        if (!rules.Walks)
        {
            lost.Travelling = false;
            return;
        }

        Vec3d here = player.Entity.Pos.XYZ;
        Vec3d? ground = places.Ground(rules, here);
        if (ground == null && !lost.AtSpawn)
        {
            // Died at sea or in a lake of lava.
            MoveToSpawn(player, lost);
            TravelToStart(player, lost, rules);
            return;
        }

        if (ground != null && ground.DistanceTo(here) > 1)
        {
            player.Entity.TeleportToDouble(ground.X, ground.Y, ground.Z);
            lost.Start = LostPlayer.Pack(ground);
        }

        lost.Travelling = false;
        lost.LandedAtUtc = DateTime.UtcNow;
        control.MakeMortal(player);
    }

    private static void MoveToSpawn(IServerPlayer player, LostPlayer lost)
    {
        lost.AtSpawn = true;
        lost.Start = LostPlayer.Pack(player.GetSpawnPosition(false).XYZ);
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
        if (afterlife.Rules(lost).Form == Form.Ban)
        {
            return;
        }

        if (afterlife.EndUtc(lost) is { } at)
        {
            messages.Tell(player, "ReturnAt", ("time", Messages.Time(at)));
        }
        else
        {
            messages.Tell(player, "NoReturn");
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

            RebirthConfig rules = afterlife.Rules(lost);
            PlayerControl.SetNoClip(player, rules.Form == Form.Spectator);
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

    // ---------- time ----------

    /// <summary>Called every few seconds.</summary>
    public void CheckReturns()
    {
        foreach (var (uid, lost) in afterlife.Players.ToList())
        {
            if (control.Online(uid) is not { } player)
            {
                continue;
            }

            returns.Check(uid, lost);
            if (lost.Phase == Phase.Serving)
            {
                SyncTimer(player, lost);
            }
        }
    }

    /// <summary>How long until they come back, for the timer on their screen.</summary>
    private void SyncTimer(IServerPlayer player, LostPlayer lost)
    {
        RebirthConfig rules = afterlife.Rules(lost);
        int? seconds = rules.ShowTimer && rules.Form != Form.Ban && afterlife.EndUtc(lost) is { } at
            ? (int)Math.Clamp((at - DateTime.UtcNow).TotalSeconds, 0, int.MaxValue)
            : null;
        ClientState.Set(player.Entity, ClientState.TimerSeconds, seconds);
        ClientState.Set(player.Entity, ClientState.TimerText, seconds == null ? null : config.Message("Timer"));
    }
}
