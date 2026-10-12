using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Server-side lifecycle of a player who runs out of lives:
/// death -> ghost (spectator) -> back to survival, either after the configured wait or by a ritual at a rift.
/// </summary>
internal class Rebirth
{
    private readonly ICoreServerAPI api;
    private readonly RebirthConfig config;
    private readonly Afterlife afterlife;
    private readonly Dictionary<string, long> lastTetherMessageMs = new();

    /// <summary>Caster uid -> ghost uid chosen with /rebirth target. Not persisted; a restart clears choices.</summary>
    private readonly Dictionary<string, string> ritualTargets = new();

    public Rebirth(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife)
    {
        this.api = api;
        this.config = config;
        this.afterlife = afterlife;
    }

    public Afterlife Afterlife => afterlife;

    public void OnPlayerNowPlaying(IServerPlayer player)
    {
        LifeRules.Sync(player);

        Ghost? ghost = afterlife.Get(player.PlayerUID);
        if (ghost == null)
        {
            // Died on their last life and left before becoming a ghost: they would be stuck on the death screen.
            if (player.Entity is { Alive: false } && !LifeRules.UnlimitedLives(api.World) && LifeRules.GetLivesLeft(player) == 0)
            {
                api.World.RegisterCallback(_ => MakeGhost(player), 1500);
            }

            return;
        }

        // Covers players who logged out as ghosts and were put back in some other mode meanwhile.
        if (player.WorldData.CurrentGameMode != EnumGameMode.Spectator)
        {
            SetGameMode(player, EnumGameMode.Spectator);
        }

        CheckReturn(player, ghost);
        if (afterlife.IsGhost(player.PlayerUID))
        {
            TellWhenReturn(player, ghost);
        }
    }

    public void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        // Vanilla increments WorldData.Deaths before raising PlayerDeath.
        LifeRules.Sync(player);

        if (LifeRules.UnlimitedLives(api.World) || LifeRules.GetLivesLeft(player) > 0)
        {
            return;
        }

        // The vanilla death screen opens 2.5 s after death; turn the player into a ghost before that.
        api.World.RegisterCallback(_ => MakeGhost(player), 1500);
    }

    private void MakeGhost(IServerPlayer player)
    {
        EntityPlayer? entity = player.Entity;
        if (entity == null || entity.Alive || LifeRules.GetLivesLeft(player) != 0)
        {
            return;
        }

        Vec3d deathPos = entity.Pos.XYZ;
        entity.Revive();
        DownedCompat.ClearDeathFlags(entity);
        SetGameMode(player, EnumGameMode.Spectator);
        afterlife.Add(player.PlayerUID, player.PlayerName, deathPos);

        player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:became-ghost");
        Ghost ghost = afterlife.Get(player.PlayerUID)!;
        TellWhenReturn(player, ghost);
        Broadcast("temporalrebirth:broadcast-ghost", player.PlayerName);
    }

    /// <summary>
    /// Called every second. A ghost that drifted beyond the tether radius is pulled back onto its edge,
    /// on the line towards the death spot, so it keeps facing where it was heading.
    /// </summary>
    public void EnforceTethers()
    {
        double radius = config.GhostTetherRadius;
        if (radius <= 0)
        {
            return;
        }

        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            Ghost? ghost = afterlife.Get(player.PlayerUID);
            if (ghost?.DeathPos is not { Length: 3 } dp || player.Entity == null || player.ConnectionState != EnumClientState.Playing)
            {
                continue;
            }

            var anchor = new Vec3d(dp[0], dp[1], dp[2]);
            Vec3d pos = player.Entity.Pos.XYZ;
            double dist = pos.DistanceTo(anchor);
            if (dist <= radius)
            {
                continue;
            }

            // Teleports far away (e.g. an admin /tp) snap straight back to the death spot.
            Vec3d target = dist > radius * 4
                ? anchor
                : anchor.AddCopy((pos.X - anchor.X) / dist * (radius - 1), (pos.Y - anchor.Y) / dist * (radius - 1), (pos.Z - anchor.Z) / dist * (radius - 1));

            player.Entity.TeleportToDouble(target.X, target.Y, target.Z);

            long now = api.World.ElapsedMilliseconds;
            if (!lastTetherMessageMs.TryGetValue(player.PlayerUID, out long last) || now - last > 15_000)
            {
                lastTetherMessageMs[player.PlayerUID] = now;
                player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ghost-tethered");
            }
        }
    }

    /// <summary>Called periodically for online ghosts.</summary>
    public void CheckReturns()
    {
        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            Ghost? ghost = afterlife.Get(player.PlayerUID);
            if (ghost != null && player.ConnectionState == EnumClientState.Playing)
            {
                CheckReturn(player, ghost);
            }
        }
    }

    private void CheckReturn(IServerPlayer player, Ghost ghost)
    {
        DateTime? returnAt = ReturnAtUtc(ghost);
        if (returnAt == null || DateTime.UtcNow < returnAt)
        {
            return;
        }

        FuzzyEntityPos spawn = player.GetSpawnPosition(false);
        Resurrect(player, spawn.XYZ, true, "temporalrebirth:returned");
    }

    public DateTime? ReturnAtUtc(Ghost ghost)
    {
        switch (config.ReturnMode)
        {
            case ReturnMode.Hours:
                return ghost.DiedAtUtc.AddHours(config.ReturnAfterHours);
            case ReturnMode.NextDay:
                DateTime diedLocal = ghost.DiedAtUtc.ToLocalTime();
                int hour = Math.Clamp(config.NextDayResetHour, 0, 23);
                return diedLocal.Date.AddDays(1).AddHours(hour).ToUniversalTime();
            default:
                return null;
        }
    }

    private void TellWhenReturn(IServerPlayer player, Ghost ghost)
    {
        DateTime? returnAt = ReturnAtUtc(ghost);
        if (returnAt == null)
        {
            player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ghost-no-return");
        }
        else
        {
            player.SendLocalisedMessage(
                GlobalConstants.GeneralChatGroup,
                "temporalrebirth:ghost-return-at",
                returnAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            );
        }
    }

    /// <summary>Ghosts who are online right now; only they can be brought back by a ritual.</summary>
    public List<IServerPlayer> OnlineGhosts() =>
        api.World.AllOnlinePlayers
            .Cast<IServerPlayer>()
            .Where(p => p.ConnectionState == EnumClientState.Playing && afterlife.IsGhost(p.PlayerUID))
            .OrderBy(p => afterlife.Get(p.PlayerUID)!.DiedAtUtc)
            .ToList();

    /// <summary>Remembers whom <paramref name="caster"/> wants to bring back with their next ritual.</summary>
    public void ChooseRitualTarget(IServerPlayer caster, IServerPlayer ghost)
    {
        ritualTargets[caster.PlayerUID] = ghost.PlayerUID;
    }

    /// <summary>
    /// Ritual performed by <paramref name="caster"/>. Only online ghosts can be revived. With one online
    /// ghost it is them; with more, the caster must pick one first (/rebirth target). Returns false when
    /// nobody was revived; the caller keeps the gear in that case.
    /// </summary>
    public bool PerformRitual(IServerPlayer caster, BlockPos ritualPos)
    {
        List<IServerPlayer> ghosts = OnlineGhosts();
        if (ghosts.Count == 0)
        {
            caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-nobody");
            return false;
        }

        IServerPlayer? target = null;
        if (ritualTargets.TryGetValue(caster.PlayerUID, out string? chosenUid))
        {
            target = ghosts.FirstOrDefault(g => g.PlayerUID == chosenUid);
        }

        if (target == null && ghosts.Count == 1)
        {
            target = ghosts[0];
        }

        if (target == null)
        {
            string names = string.Join(", ", ghosts.Select(g => g.PlayerName));
            caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-choose", names);
            return false;
        }

        ritualTargets.Remove(caster.PlayerUID);
        string ghostName = target.PlayerName;

        // The ritual has its own broadcast below.
        Resurrect(target, caster.Entity.Pos.XYZ, false, "temporalrebirth:revived-ritual", caster.PlayerName);

        caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-done", ghostName);
        Broadcast("temporalrebirth:broadcast-ritual", caster.PlayerName, ghostName);
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), ritualPos.X + 0.5, ritualPos.Y + 0.5, ritualPos.Z + 0.5, null, false, 48f);
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual at a rift near {2}", caster.PlayerName, ghostName, ritualPos);
        return true;
    }

    /// <summary>Admin command path; also works for online players who are not ghosts but have 0 lives.</summary>
    public void AdminRevive(IServerPlayer player)
    {
        Resurrect(player, player.Entity.Pos.XYZ, true, "temporalrebirth:revived-admin");
    }

    private void Resurrect(IServerPlayer player, Vec3d at, bool announce, string messageKey, params object[] args)
    {
        LifeRules.GrantSecondChance(player);
        afterlife.Remove(player.PlayerUID);

        EntityPlayer entity = player.Entity;
        if (!entity.Alive)
        {
            entity.Revive();
        }

        DownedCompat.ClearDeathFlags(entity);
        SetGameMode(player, EnumGameMode.Survival);
        entity.TeleportToDouble(at.X, at.Y, at.Z);
        LifeRules.Sync(player);

        player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, messageKey, args);
        if (announce)
        {
            Broadcast("temporalrebirth:broadcast-returned", player.PlayerName);
        }
    }

    /// <summary>Goes through the vanilla command so every server system sees the change like an admin switch.</summary>
    private void SetGameMode(IServerPlayer player, EnumGameMode mode)
    {
        string modeName = mode == EnumGameMode.Spectator ? "spectator" : "survival";
        var args = new TextCommandCallingArgs
        {
            Caller = new Caller
            {
                Type = EnumCallerType.Console,
                CallerRole = "admin",
                CallerPrivileges = ["*"],
                FromChatGroupId = GlobalConstants.ConsoleGroup,
            },
        };

        api.ChatCommands.ExecuteUnparsed(
            $"/player {player.PlayerName} gamemode {modeName}",
            args,
            result =>
            {
                if (result.Status != EnumCommandStatus.Success)
                {
                    api.Logger.Error("[temporalrebirth] Could not set {0} to {1}: {2}", player.PlayerName, modeName, result.StatusMessage);
                }
            }
        );
    }

    private void Broadcast(string langKey, params object[] args)
    {
        foreach (IServerPlayer p in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            p.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, langKey, args);
        }
    }
}
