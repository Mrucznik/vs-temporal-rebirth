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

        if (ghost.RevivedByRitual)
        {
            Resurrect(player, new Vec3d(ghost.RitualX, ghost.RitualY, ghost.RitualZ), true, "temporalrebirth:revived-ritual-offline");
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

        entity.Revive();
        DownedCompat.ClearDeathFlags(entity);
        SetGameMode(player, EnumGameMode.Spectator);
        afterlife.Add(player.PlayerUID, player.PlayerName);

        player.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:became-ghost");
        Ghost ghost = afterlife.Get(player.PlayerUID)!;
        TellWhenReturn(player, ghost);
        Broadcast("temporalrebirth:broadcast-ghost", player.PlayerName);
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

    /// <summary>
    /// Ritual performed by <paramref name="caster"/>. Returns false when there is nobody to revive;
    /// the caller keeps the gear in that case.
    /// </summary>
    public bool PerformRitual(IServerPlayer caster, BlockPos ritualPos)
    {
        KeyValuePair<string, Ghost>? target = afterlife.NextRitualTarget(
            config.RitualRevivesOffline,
            uid => api.World.PlayerByUid(uid) is IServerPlayer p && p.ConnectionState == EnumClientState.Playing
        );

        if (target == null)
        {
            caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-nobody");
            return false;
        }

        string uid = target.Value.Key;
        Ghost ghost = target.Value.Value;
        Vec3d at = caster.Entity.Pos.XYZ;

        if (api.World.PlayerByUid(uid) is IServerPlayer ghostPlayer && ghostPlayer.ConnectionState == EnumClientState.Playing)
        {
            // The ritual has its own broadcast below.
            Resurrect(ghostPlayer, at, false, "temporalrebirth:revived-ritual", caster.PlayerName);
        }
        else
        {
            ghost.RevivedByRitual = true;
            ghost.RitualX = at.X;
            ghost.RitualY = at.Y;
            ghost.RitualZ = at.Z;
            afterlife.Save();
        }

        caster.SendLocalisedMessage(GlobalConstants.GeneralChatGroup, "temporalrebirth:ritual-done", ghost.Name);
        Broadcast("temporalrebirth:broadcast-ritual", caster.PlayerName, ghost.Name);
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), ritualPos.X + 0.5, ritualPos.Y + 0.5, ritualPos.Z + 0.5, null, false, 48f);
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual at a rift near {2}", caster.PlayerName, ghost.Name, ritualPos);
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
