using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// Client side of holding a gear. A hold that is a ritual is ours from start to end: vanilla's would predict the gear
/// being used up and flash as if the spawn point was set, before the server decided anything.
/// </summary>
[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStart))]
internal static class GearClientStartPatch
{
    private static bool Prefix(EntityAgent byEntity, BlockSelection blockSel, bool firstEvent, ref EnumHandHandling handHandling) =>
        Own(byEntity) is not { } ritual || ritual.OnHoldStart(blockSel, firstEvent, ref handHandling);

    /// <summary>The ritual client, when this is the own player on the client.</summary>
    internal static RitualClient? Own(EntityAgent byEntity) =>
        byEntity.World.Side == EnumAppSide.Client && TemporalRebirthModSystem.ClientRitual is { } ritual && byEntity is EntityPlayer { Api: ICoreClientAPI capi } player
        && player == capi.World.Player?.Entity ? ritual : null;
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStep))]
internal static class GearClientStepPatch
{
    private static bool Prefix(ItemTemporalGear __instance, float secondsUsed, EntityAgent byEntity, BlockSelection blockSel, ref bool __result)
    {
        if (GearClientStartPatch.Own(byEntity) is not { Holding: true } ritual)
        {
            return true;
        }

        ritual.StepHold(__instance, secondsUsed);
        __result = blockSel != null && secondsUsed < RitualLook.HoldSeconds;
        return false;
    }
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStop))]
internal static class GearClientStopPatch
{
    /// <summary>The server answers: with a revival, the chooser, or a message and the gear left in hand.</summary>
    private static bool Prefix(EntityAgent byEntity)
    {
        if (GearClientStartPatch.Own(byEntity) is not { Holding: true } ritual)
        {
            return true;
        }

        ritual.EndHold();
        return false;
    }
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractCancel))]
internal static class GearClientCancelPatch
{
    private static bool Prefix(EntityAgent byEntity, ref bool __result)
    {
        if (GearClientStartPatch.Own(byEntity) is not { Holding: true } ritual)
        {
            return true;
        }

        ritual.EndHold();
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.GetHeldInteractionHelp))]
internal static class GearClientHelpPatch
{
    private static void Postfix(ref WorldInteraction[] __result)
    {
        if (TemporalRebirthModSystem.ClientRitual is { } ritual)
        {
            __result = ritual.Help(__result);
        }
    }
}
