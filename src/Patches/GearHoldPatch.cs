using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth.Patches;

/// <summary>
/// Server side of holding a gear that is a ritual. The start is ours: vanilla refuses to start at all in worlds where
/// gears cannot set the spawn point, which would make rituals impossible there.
/// </summary>
[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStart))]
internal static class GearHoldStartPatch
{
    private static bool Prefix(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handHandling)
    {
        if (Caster(byEntity) is not { } caster || TemporalRebirthModSystem.Server is not { } server)
        {
            return true;
        }

        if (blockSel == null)
        {
            // Aimed at a ghost in the air: a ritual or nothing at all.
            if (LifeRules.UnlimitedLives(byEntity.World) || !server.Rituals.BeginHold(caster, null))
            {
                return true;
            }

            handHandling = EnumHandHandling.PreventDefault;
            return false;
        }

        // Vanilla ignores gears used on a static translocator (it repairs them instead).
        if (byEntity.World.BlockAccessor.GetBlock(blockSel.Position) is BlockStaticTranslocator || LifeRules.UnlimitedLives(byEntity.World))
        {
            return true;
        }

        if (!server.Rituals.BeginHold(caster, blockSel))
        {
            // Where gears cannot set the spawn point vanilla would not start; a client that saw a ritual here a moment
            // ago would then hold in silence. Held to the end, the caster hears why nothing happened.
            if (byEntity.World.Config.GetAsInt("temporalGearRespawnUses", -1) != 0 || !server.Rituals.IsRitualSite(caster))
            {
                return true;
            }
        }

        handHandling = EnumHandHandling.PreventDefault;
        return false;
    }

    internal static IServerPlayer? Caster(EntityAgent byEntity) =>
        byEntity.World.Side == EnumAppSide.Server && byEntity is EntityPlayer entity ? entity.World.PlayerByUid(entity.PlayerUID) as IServerPlayer : null;
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStep))]
internal static class GearHoldStepPatch
{
    /// <summary>Vanilla stops a hold without a block at once; one aimed at a ghost in the air goes on.</summary>
    private static bool Prefix(float secondsUsed, EntityAgent byEntity, BlockSelection blockSel, ref bool __result)
    {
        if (blockSel != null || GearHoldStartPatch.Caster(byEntity) is not { } caster || TemporalRebirthModSystem.Server?.Rituals is not { } rituals
            || !rituals.IsAirHold(caster))
        {
            return true;
        }

        __result = secondsUsed < RitualLook.HoldSeconds;
        return false;
    }

    private static void Postfix(float secondsUsed, EntityAgent byEntity)
    {
        if (GearHoldStartPatch.Caster(byEntity) is { } caster)
        {
            TemporalRebirthModSystem.Server?.Rituals.StepHold(caster, secondsUsed);
        }
    }
}

[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractCancel))]
internal static class GearHoldCancelPatch
{
    private static void Postfix(EntityAgent byEntity)
    {
        if (GearHoldStartPatch.Caster(byEntity) is { } caster)
        {
            TemporalRebirthModSystem.Server?.Rituals.EndHold(caster);
        }
    }
}
