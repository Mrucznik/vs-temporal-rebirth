using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.Server;

namespace TemporalRebirth.Patches;

/// <summary>
/// Vanilla refuses a respawn once playerlives &lt;= deaths. Count our extra lives too.
/// Players with no lives left never reach this: they become ghosts before the death screen opens.
/// </summary>
[HarmonyPatch(typeof(ServerSystemEntitySimulation), "HandleSpecialKey")]
internal static class RespawnPatch
{
    private static bool Prefix(ServerMain ___server, ConnectedClient client)
    {
        if (LifeRules.UnlimitedLives(___server) || LifeRules.GetLivesLeft(client.Player) != 0)
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
