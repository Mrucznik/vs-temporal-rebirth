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

    /// <summary>Set while a resurrection teleport is in flight; finished on next login if the player left.</summary>
    private const string ReturningKey = "temporalrebirth.returning";

    /// <summary>UTC ticks of the last ritual revival, for <see cref="RebirthConfig.RitualCooldownHours"/>.</summary>
    private const string LastRitualKey = "temporalrebirth.lastritual";

    /// <summary>Set while a weakened return is pending; applied once the player is alive at the destination.</summary>
    private const string WeakenKey = "temporalrebirth.weaken";

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
        LifeRules.EnsureBaseline(player);
        LifeRules.Sync(player);

        if (player.GetModData(ReturningKey, false))
        {
            // Left mid-teleport: no longer a ghost, but the switch back to survival never happened.
            FinishReturn(player);
            return;
        }

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

        // A ghost left sitting on a mount or in a boat could keep steering it.
        entity.TryUnmount();

        Vec3d deathPos = entity.Pos.XYZ;
        if (!config.GhostNoClip)
        {
            // Died inside a block (suffocation, sand): a solid ghost would be stuck there.
            deathPos = SafeSpot.FindAbove(api.World.BlockAccessor, deathPos) ?? deathPos;
        }

        entity.Revive();
        DownedCompat.ClearDeathFlags(entity);
        SetGameMode(player, EnumGameMode.Spectator);
        if (!entity.Pos.XYZ.Equals(deathPos))
        {
            entity.TeleportToDouble(deathPos.X, deathPos.Y, deathPos.Z);
        }

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

        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            Ghost? ghost = afterlife.Get(player.PlayerUID);
            if (ghost == null || player.Entity == null || player.ConnectionState != EnumClientState.Playing)
            {
                continue;
            }

            KeepNoClipSetting(player);

            if (ghost.DeathPos is not { Length: 3 } dp)
            {
                continue;
            }

            if (radius <= 0)
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

            // The pull-back line can cross a hill; a solid ghost must not land inside it.
            if (!config.GhostNoClip && !SafeSpot.IsFree(api.World.BlockAccessor, target))
            {
                target = SafeSpot.FindAbove(api.World.BlockAccessor, target)
                    ?? SafeSpot.FindAbove(api.World.BlockAccessor, anchor)
                    ?? anchor;
            }

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
        // The world went back to unlimited lives (or the mod disabled itself): nobody should stay a ghost.
        bool released = LifeRules.UnlimitedLives(api.World);

        DateTime? returnAt = ReturnAtUtc(ghost);
        if (!released && (returnAt == null || DateTime.UtcNow < returnAt))
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

        DateTime? readyAt = RitualReadyAtUtc(target);
        if (readyAt != null && DateTime.UtcNow < readyAt)
        {
            caster.SendLocalisedMessage(
                GlobalConstants.GeneralChatGroup,
                "temporalrebirth:ritual-cooldown",
                target.PlayerName,
                readyAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            );
            return false;
        }

        ritualTargets.Remove(caster.PlayerUID);
        string ghostName = target.PlayerName;
        target.SetModData(LastRitualKey, DateTime.UtcNow.Ticks);

        // The ritual has its own broadcast below.
        Resurrect(target, RitualDestination(caster, target), false, "temporalrebirth:revived-ritual", caster.PlayerName);

        caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-done", ghostName);
        Broadcast("temporalrebirth:broadcast-ritual", caster.PlayerName, ghostName);
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), ritualPos.X + 0.5, ritualPos.Y + 0.5, ritualPos.Z + 0.5, null, false, 48f);
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual at a rift near {2}", caster.PlayerName, ghostName, ritualPos);
        return true;
    }

    private Vec3d RitualDestination(IServerPlayer caster, IServerPlayer ghost)
    {
        switch (config.RitualReviveAt)
        {
            case RitualReviveAt.DeathSpot:
                if (afterlife.Get(ghost.PlayerUID)?.DeathPos is { Length: 3 } dp)
                {
                    return new Vec3d(dp[0], dp[1], dp[2]);
                }

                return ghost.GetSpawnPosition(false).XYZ;
            case RitualReviveAt.Spawn:
                return ghost.GetSpawnPosition(false).XYZ;
            default:
                return caster.Entity.Pos.XYZ;
        }
    }

    private DateTime? RitualReadyAtUtc(IServerPlayer ghost)
    {
        if (config.RitualCooldownHours <= 0)
        {
            return null;
        }

        long ticks = ghost.GetModData(LastRitualKey, 0L);
        return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc).AddHours(config.RitualCooldownHours);
    }

    /// <summary>Admin command path; also works for online players who are not ghosts but have 0 lives.</summary>
    public void AdminRevive(IServerPlayer player)
    {
        // Admin revives are a fix-up tool: no weakness.
        Resurrect(player, player.Entity.Pos.XYZ, true, "temporalrebirth:revived-admin", weaken: false);
    }

    private void Resurrect(IServerPlayer player, Vec3d at, bool announce, string messageKey, params object[] args) =>
        Resurrect(player, at, announce, messageKey, true, args);

    private void Resurrect(IServerPlayer player, Vec3d at, bool announce, string messageKey, bool weaken, params object[] args)
    {
        LifeRules.GrantSecondChance(player);
        // Removed first so the tether tick leaves the player alone while the teleport is in flight.
        afterlife.Remove(player.PlayerUID);
        LifeRules.Sync(player);

        // Same order as vanilla respawn: travel as a ghost, wait for the chunks at the destination,
        // and only then become mortal, so nobody falls out of the sky over unloaded terrain.
        player.SetModData(ReturningKey, true);
        player.SetModData(WeakenKey, weaken);
        EntityPlayer entity = player.Entity;
        EntityPos target = entity.Pos.Copy();
        target.SetPos(at);
        entity.TeleportTo(target, () =>
        {
            FinishReturn(player);
            player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, messageKey, args);
            if (announce)
            {
                Broadcast("temporalrebirth:broadcast-returned", player.PlayerName);
            }
        });
    }

    private void FinishReturn(IServerPlayer player)
    {
        EntityPlayer entity = player.Entity;
        if (!entity.Alive)
        {
            entity.Revive();
        }

        DownedCompat.ClearDeathFlags(entity);
        SetGameMode(player, EnumGameMode.Survival);
        player.SetModData(ReturningKey, false);

        if (player.GetModData(WeakenKey, false))
        {
            Weaken(entity);
            player.SetModData(WeakenKey, false);
        }
    }

    /// <summary>
    /// Applies <see cref="RebirthConfig.ReturnHealthPercent"/> and <see cref="RebirthConfig.ReturnSatietyPercent"/>
    /// through the vanilla health and hunger attribute trees (EntityBehaviorHealth / EntityBehaviorHunger).
    /// </summary>
    private void Weaken(EntityPlayer entity)
    {
        ScaleTree(entity, "health", "currenthealth", "maxhealth", config.ReturnHealthPercent, minimum: 1f);
        ScaleTree(entity, "hunger", "currentsaturation", "maxsaturation", config.ReturnSatietyPercent, minimum: 0f);
    }

    private static void ScaleTree(EntityPlayer entity, string tree, string currentKey, string maxKey, float percent, float minimum)
    {
        if (percent >= 100)
        {
            return;
        }

        var attrs = entity.WatchedAttributes.GetTreeAttribute(tree);
        if (attrs == null)
        {
            return;
        }

        float max = attrs.GetFloat(maxKey, 0f);
        if (max <= 0)
        {
            return;
        }

        // Only ever lowers: vanilla revive already leaves satiety at 50%, and a cap of e.g. 70% must not raise it.
        float cap = Math.Max(minimum, max * Math.Clamp(percent, 0f, 100f) / 100f);
        if (attrs.GetFloat(currentKey, 0f) > cap)
        {
            attrs.SetFloat(currentKey, cap);
            entity.WatchedAttributes.MarkPathDirty(tree);
        }
    }

    /// <summary>
    /// Applies <see cref="RebirthConfig.GhostNoClip"/>. Spectator mode turns no-clip on and the client
    /// can toggle it, so this also runs on every tether tick.
    /// </summary>
    private void KeepNoClipSetting(IServerPlayer player)
    {
        if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator && player.WorldData.NoClip != config.GhostNoClip)
        {
            player.WorldData.NoClip = config.GhostNoClip;
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

        api.ChatCommands.ExecuteUnparsed(
            $"/player {player.PlayerName} gamemode {modeName}",
            args,
            result =>
            {
                if (result.Status != EnumCommandStatus.Success)
                {
                    api.Logger.Error("[temporalrebirth] Could not set {0} to {1}: {2}", player.PlayerName, modeName, result.StatusMessage);
                    return;
                }

                if (mode == EnumGameMode.Spectator)
                {
                    KeepNoClipSetting(player);
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
