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

        // Vanilla ignores gears used on a static translocator (it repairs them instead).
        if (blockSel == null || byEntity.World.BlockAccessor.GetBlock(blockSel.Position) is BlockStaticTranslocator || LifeRules.UnlimitedLives(byEntity.World))
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
