using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace TemporalRebirth.Patches;

/// <summary>The client only knows vanilla lives; show the count the server synced instead.</summary>
[HarmonyPatch(typeof(GuiDialogDead), "ComposeDialog")]
internal static class DeathDialogPatch
{
    private static void Prefix(ClientMain ___game, ref int ___livesLeft)
    {
        var attrs = ___game.EntityPlayer?.WatchedAttributes;
        if (attrs != null && attrs.HasAttribute(LifeRules.LivesLeftAttr))
        {
            ___livesLeft = attrs.GetInt(LifeRules.LivesLeftAttr);
        }
    }
}
