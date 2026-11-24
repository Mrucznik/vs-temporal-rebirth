using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>Moves and changes players. Knows nothing about lives.</summary>
internal class PlayerControl(ICoreServerAPI api)
{
    /// <summary>The latest teleport of each player; older ones that finish late are ignored.</summary>
    private readonly Dictionary<string, int> journeys = new();

    /// <summary>An online player with an entity, at any point after joining.</summary>
    public IServerPlayer? Online(string uid) =>
        api.World.PlayerByUid(uid) is IServerPlayer { Entity: not null, ConnectionState: EnumClientState.Connected or EnumClientState.Playing } player ? player : null;

    /// <summary>Calls back when the land there is loaded and the player is on it.</summary>
    public void GoTo(IServerPlayer player, Vec3d pos, Action arrived)
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

    /// <summary>Alive again where they are. Downed would remember that they died.</summary>
    public static bool Revive(EntityPlayer entity)
    {
        if (entity.Alive)
        {
            return false;
        }

        entity.Revive();
        DownedCompat.ClearDeathFlags(entity);
        return true;
    }

    /// <summary>Ends being downed without dying of it.</summary>
    public void StandUp(IServerPlayer player)
    {
        if (DownedCompat.IsDowned(player.Entity))
        {
            RunFor(player, $"/downed revive {player.PlayerName}", _ => { });
        }
    }

    /// <summary>Spectators who may pass through blocks; the client can toggle it, so call this again and again.</summary>
    public static void SetNoClip(IServerPlayer player, bool noClip)
    {
        if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator && player.WorldData.NoClip != noClip)
        {
            player.WorldData.NoClip = noClip;
            player.BroadcastPlayerData(false);
        }
    }

    public void MakeMortal(IServerPlayer player) => SetGameMode(player, EnumGameMode.Survival, () => { });

    /// <summary>Spectator mode turns passing through blocks on.</summary>
    public void MakeSpectator(IServerPlayer player, bool noClip) =>
        SetGameMode(player, EnumGameMode.Spectator, () => SetNoClip(player, noClip));

    /// <summary>
    /// Goes through the vanilla command so every server system sees the change like an admin switch.
    /// The caller is the player themself: vanilla then skips its "Console Admin has set your gamemode"
    /// chat line, which is not translated on the server and would clash with our localized messages.
    /// </summary>
    private void SetGameMode(IServerPlayer player, EnumGameMode mode, Action done)
    {
        if (player.WorldData.CurrentGameMode == mode)
        {
            return;
        }

        string modeName = mode == EnumGameMode.Spectator ? "spectator" : "survival";
        RunFor(player, $"/player {player.PlayerName} gamemode {modeName}", result =>
        {
            if (result.Status != EnumCommandStatus.Success)
            {
                api.Logger.ModError("Could not set {0} to {1}: {2}", player.PlayerName, modeName, result.StatusMessage);
            }
            else
            {
                done();
            }
        });
    }

    private void RunFor(IServerPlayer player, string command, Action<TextCommandResult> done)
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

    /// <summary>Sets a vital to an exact percentage of its maximum; -1 leaves what the game gave.</summary>
    public static void SetVital(EntityPlayer entity, string tree, string currentKey, string maxKey, int percent, float minimum)
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
}
