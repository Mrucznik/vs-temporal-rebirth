using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>A zombie that may not use items: no held items, no picking up, no blocks such as containers or doors.</summary>
internal class ZombieRules(ICoreServerAPI api, Afterlife afterlife, Rebirth rebirth)
{
    /// <summary>The Harmony patch is static; it reaches the server state through this.</summary>
    internal static ZombieRules? Current { get; set; }

    public void Register()
    {
        Current = this;
        api.Event.CanUseBlock += (player, _) => !IsBound(player.PlayerUID);
        api.Event.CanPlaceOrBreakBlock += CanPlaceOrBreak;
        api.Event.HandInteract += OnHandInteract;
    }

    public bool IsBound(string uid) =>
        afterlife.Get(uid) is { Phase: Phase.Serving } lost && rebirth.Rules(lost) is { Form: Form.Zombie, ZombieCanUseItems: false };

    private bool CanPlaceOrBreak(IServerPlayer player, BlockSelection selection, out string? claimant)
    {
        claimant = null;
        return !IsBound(player.PlayerUID);
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
        if (__instance.entity is not EntityPlayer player || ZombieRules.Current?.IsBound(player.PlayerUID) != true)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
