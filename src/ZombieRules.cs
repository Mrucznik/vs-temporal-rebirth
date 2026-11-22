using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// A zombie that may not use items: no held items, no picking up, no blocks such as containers or doors,
/// no mounts or other creatures. Bare hands may fight.
/// </summary>
internal class ZombieRules(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife)
{
    public void Register()
    {
        api.Event.CanUseBlock += (player, _) => !IsBound(player.PlayerUID);
        api.Event.CanPlaceOrBreakBlock += CanPlaceOrBreak;
        api.Event.HandInteract += OnHandInteract;
        api.Event.OnPlayerInteractEntity += OnInteractEntity;
    }

    public bool IsBound(string uid) =>
        afterlife.Get(uid) is { Phase: Phase.Serving } lost && lost.RulesIn(config) is { Form: Form.Zombie, ZombieCanUseItems: false };

    private bool CanPlaceOrBreak(IServerPlayer player, BlockSelection selection, out string? claimant)
    {
        claimant = null;
        return !IsBound(player.PlayerUID);
    }

    private void OnInteractEntity(Entity entity, IPlayer player, ItemSlot? slot, Vec3d hit, int mode, ref EnumHandling handling)
    {
        bool bareHandedAttack = mode == (int)EnumInteractMode.Attack && slot?.Empty != false;
        if (!bareHandedAttack && IsBound(player.PlayerUID))
        {
            handling = EnumHandling.PreventSubsequent;
        }
    }

    private void OnHandInteract(IServerPlayer player, EnumHandInteractNw interaction, float seconds, ref EnumHandling handling)
    {
        if (IsBound(player.PlayerUID))
        {
            handling = EnumHandling.PreventSubsequent;
        }
    }
}

[HarmonyPatch(typeof(EntityBehaviorCollectEntities), nameof(EntityBehaviorCollectEntities.OnFoundCollectible))]
internal static class ZombiePickupPatch
{
    private static bool Prefix(EntityBehaviorCollectEntities __instance, ref bool __result)
    {
        if (__instance.entity is not EntityPlayer player || TemporalRebirthModSystem.Server?.Zombies.IsBound(player.PlayerUID) != true)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
