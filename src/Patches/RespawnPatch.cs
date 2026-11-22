using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace TemporalRebirth.Patches;

/// <summary>
/// Vanilla refuses a respawn once playerlives &lt;= deaths. Count our extra lives too.
/// Players with no lives left never reach this: they become something else before the death screen opens.
/// </summary>
[HarmonyPatch(typeof(ServerSystemEntitySimulation), "HandleSpecialKey")]
internal static class RespawnPatch
{
    private static bool IsAllowed(IServerPlayer player) =>
        TemporalRebirthModSystem.Server?.Afterlife.Contains(player.PlayerUID) != true
        && (LifeRules.GetLivesLeft(player) != 0 || LifeRules.MayRespawn(player.Entity));

    private static bool Prefix(ServerMain ___server, ConnectedClient client)
    {
        // Between dying and the game counting the death, the life just lost still looks available.
        if (client.Entityplayer is { DeadNotify: true } && !LifeRules.UnlimitedLives(___server))
        {
            return false;
        }

        if (LifeRules.UnlimitedLives(___server) || IsAllowed(client.Player))
        {
            ServerMain.Logger.VerboseDebug("Received respawn request from {0}", client.PlayerName);
            ___server.EventManager.TriggerPlayerRespawn(client.Player);
        }
        else
        {
            client.Player.SendMessage(GlobalConstants.CurrentChatGroup, "Cannot revive! All lives used up.", Vintagestory.API.Common.EnumChatType.CommandError);
        }

        return false;
    }
}
