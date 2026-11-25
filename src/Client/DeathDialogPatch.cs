using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace TemporalRebirth.Patches;

/// <summary>
/// The client only knows vanilla lives (playerlives - all-time deaths). Show the count the server
/// synced instead, which includes lives earned from gears and only counts deaths in the current epoch.
/// The texts are the mod's own: the game's English says "1 lives left", and at no lives with a respawn (downs count
/// lives) the game's plain "you died" did not say that the next fatal hit is the last.
/// </summary>
[HarmonyPatch(typeof(GuiDialogDead))]
internal static class DeathDialogPatch
{
    private const string GameDied = "Congratulations, you died!", GameLivesLeft = "Congratulations, you died! {0} lives left.";

    /// <summary>Set while composing: no lives, yet a respawn (see <see cref="TryGetSyncedLives"/>).</summary>
    private static bool lastChance;

    [HarmonyTranspiler]
    [HarmonyPatch("ComposeDialog")]
    private static IEnumerable<CodeInstruction> Texts(IEnumerable<CodeInstruction> code)
    {
        foreach (CodeInstruction op in code)
        {
            if (op.opcode == OpCodes.Ldstr && op.operand is GameDied)
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeathDialogPatch), nameof(DiedKey))).MoveLabelsFrom(op);
            }
            else if (op.opcode == OpCodes.Ldstr && op.operand is GameLivesLeft)
            {
                yield return new CodeInstruction(OpCodes.Ldstr, "temporalrebirth:death-livesleft").MoveLabelsFrom(op);
            }
            else
            {
                yield return op;
            }
        }
    }

    private static string DiedKey() => lastChance ? "temporalrebirth:death-lastchance" : GameDied;

    private static readonly Action<GuiDialogDead> Recompose =
        AccessTools.MethodDelegate<Action<GuiDialogDead>>(AccessTools.Method(typeof(GuiDialogDead), "ComposeDialog"));

    [HarmonyPrefix]
    [HarmonyPatch("ComposeDialog")]
    private static void BeforeCompose(ClientMain ___game, ref int ___livesLeft)
    {
        lastChance = false;
        if (TryGetSyncedLives(___game, out int lives))
        {
            ___livesLeft = lives;
            lastChance = lives == -1 && ___game.EntityPlayer?.WatchedAttributes.GetInt(LifeRules.LivesLeftAttr) == 0;
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
        if (lives == 0 && attrs.GetBool(LifeRules.MayRespawnAttr))
        {
            // Without lives but not for good; the dialog only offers a respawn when the count is not zero.
            lives = -1;
        }

        return true;
    }
}
