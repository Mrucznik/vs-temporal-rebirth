using HarmonyLib;
using Vintagestory.Server;

namespace TemporalRebirth.Patches;

/// <summary>Turns away banned players before they load the world, with a text they actually see.</summary>
[HarmonyPatch(typeof(ServerMain), "HandlePlayerIdentification")]
internal static class JoinPatch
{
    private static bool Prefix(ServerMain __instance, Packet_Client p, ConnectedClient client)
    {
        if (TemporalRebirthModSystem.Rebirth?.RefuseJoin(p.Identification?.PlayerUID) is not { } text)
        {
            return true;
        }

        __instance.DisconnectPlayer(client, null, text);
        return false;
    }
}
