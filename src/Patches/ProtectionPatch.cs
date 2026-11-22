using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace TemporalRebirth.Patches;

/// <summary>Zombies and players out of lives cannot be hurt right after they rise, see <see cref="Rebirth.IsProtected"/>.</summary>
[HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.ShouldReceiveDamage))]
internal static class ProtectionPatch
{
    private static void Postfix(EntityPlayer __instance, DamageSource? damageSource, ref bool __result)
    {
        if (__result && damageSource?.Type != EnumDamageType.Heal && __instance.World?.Side == EnumAppSide.Server
            && TemporalRebirthModSystem.Server?.Rebirth.IsProtected(__instance.PlayerUID) == true)
        {
            __result = false;
        }
    }
}
