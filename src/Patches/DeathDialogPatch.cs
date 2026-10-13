using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace TemporalRebirth.Patches;

/// <summary>
/// The client only knows vanilla lives (playerlives - all-time deaths). Show the count the server
/// synced instead, which includes lives earned from gears and only counts deaths in the current epoch.
/// </summary>
[HarmonyPatch(typeof(GuiDialogDead))]
internal static class DeathDialogPatch
{
    private static readonly Action<GuiDialogDead> Recompose =
        AccessTools.MethodDelegate<Action<GuiDialogDead>>(AccessTools.Method(typeof(GuiDialogDead), "ComposeDialog"));

    [HarmonyPrefix]
    [HarmonyPatch("ComposeDialog")]
    private static void BeforeCompose(ClientMain ___game, ref int ___livesLeft)
    {
        if (TryGetSyncedLives(___game, out int lives))
        {
            ___livesLeft = lives;
        }
    }

    /// <summary>
    /// The synced value can change while the screen is open (it arrives late, or an admin sets lives).
    /// Rebuild the dialog so the text and the respawn button match.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("OnGameTick")]
    private static void AfterTick(GuiDialogDead __instance, ClientMain ___game, int ___livesLeft)
    {
        if (__instance.IsOpened() && TryGetSyncedLives(___game, out int lives) && lives != ___livesLeft)
        {
            Recompose(__instance);
        }
    }

    private static bool TryGetSyncedLives(ClientMain game, out int lives)
    {
        lives = 0;
        var attrs = game.EntityPlayer?.WatchedAttributes;
        if (attrs == null || !attrs.HasAttribute(LifeRules.LivesLeftAttr))
        {
            return false;
        }

        lives = attrs.GetInt(LifeRules.LivesLeftAttr);
        return true;
    }
}
