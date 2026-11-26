using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// Keeps a walking player out of lives (zombie or exile) from what their form forbids: held items, building, blocks such as
/// containers or doors, creatures, picking up, hitting. Dropping items is always allowed.
/// </summary>
internal class FormLimits(ICoreServerAPI api, Afterlife afterlife)
{
    public void Register()
    {
        api.Event.CanUseBlock += (player, _) => !Of(player.PlayerUID).Blocks;
        api.Event.CanPlaceOrBreakBlock += CanPlaceOrBreak;
        api.Event.HandInteract += OnHandInteract;
        api.Event.OnPlayerInteractEntity += OnInteractEntity;
    }

    /// <summary>What this player is kept from now; nothing for the living.</summary>
    public Limits Of(string uid) =>
        afterlife.Get(uid) is { Phase: Phase.Serving } lost ? afterlife.Rules(lost).Limits : Limits.None;

    private bool CanPlaceOrBreak(IServerPlayer player, BlockSelection selection, out string? claimant)
    {
        claimant = null;
        return !Of(player.PlayerUID).Build;
    }

    private void OnInteractEntity(Entity entity, IPlayer player, ItemSlot? slot, Vec3d hit, int mode, ref EnumHandling handling)
    {
        Limits limits = Of(player.PlayerUID);
        bool holds = slot?.Empty == false;
        bool forbidden = (mode == (int)EnumInteractMode.Attack ? limits.Hit : limits.Creatures) || (holds && limits.Items);
        if (forbidden)
        {
            handling = EnumHandling.PreventSubsequent;
        }
    }

    private void OnHandInteract(IServerPlayer player, EnumHandInteractNw interaction, float seconds, ref EnumHandling handling)
    {
        Limits limits = Of(player.PlayerUID);
        bool heldItem = interaction is EnumHandInteractNw.StartHeldItemUse or EnumHandInteractNw.StepHeldItemUse
            or EnumHandInteractNw.StopHeldItemUse or EnumHandInteractNw.CancelHeldItemUse;
        if (heldItem ? limits.Items : limits.Blocks)
        {
            handling = EnumHandling.PreventSubsequent;
        }
    }
}

[HarmonyPatch(typeof(EntityBehaviorCollectEntities), nameof(EntityBehaviorCollectEntities.OnFoundCollectible))]
internal static class PickUpLimitPatch
{
    private static bool Prefix(EntityBehaviorCollectEntities __instance, ref bool __result)
    {
        if (__instance.entity is not EntityPlayer player || TemporalRebirthModSystem.Server?.Limits.Of(player.PlayerUID).PickUp != true)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
