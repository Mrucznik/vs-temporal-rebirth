using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth.Patches;

/// <summary>
/// Holding a temporal gear (right click on a block for 3.5 s, the vanilla "set respawn point" use):
///  - at a ritual site: performs the ritual instead of setting the respawn point;
///  - anywhere else: vanilla sets the respawn point and we add extra lives on top.
/// Players out of lives get neither: a gear would be a way out.
/// </summary>
[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStop))]
internal static class TemporalGearPatch
{
    // Same threshold vanilla uses before it consumes the gear.
    private const double VanillaUseSeconds = 3.45;

    /// <returns>false to skip vanilla (the gear did something else, or nothing).</returns>
    private static bool Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, out bool __state)
    {
        __state = false;
        if (
            blockSel == null
            || secondsUsed < VanillaUseSeconds
            || byEntity.World.Side != EnumAppSide.Server
            || byEntity is not EntityPlayer playerEntity
            || playerEntity.World.PlayerByUid(playerEntity.PlayerUID) is not IServerPlayer player
            || LifeRules.UnlimitedLives(playerEntity.World)
            || TemporalRebirthModSystem.Rituals is not { } rituals
        )
        {
            return true;
        }

        Afterlife afterlife = TemporalRebirthModSystem.Rebirth!.Afterlife;
        bool lost = afterlife.Contains(player.PlayerUID);
        if (!lost && !rituals.IsRitualSite(player))
        {
            return true;
        }

        __state = true;
        if (!lost && Rituals.CanPerform(player, afterlife) && rituals.Perform(player, blockSel.Position))
        {
            slot.TakeOut(1);
        }

        // The client predicted the gear being used up; resend the slot either way.
        slot.MarkDirty();
        return false;
    }

    private static void Postfix(float secondsUsed, EntityAgent byEntity, BlockSelection blockSel, bool __state)
    {
        if (
            __state
            || blockSel == null
            || secondsUsed < VanillaUseSeconds
            || byEntity is not EntityPlayer playerEntity
            || playerEntity.World.PlayerByUid(playerEntity.PlayerUID) is not IServerPlayer player
            || LifeRules.UnlimitedLives(playerEntity.World)
            || TemporalRebirthModSystem.Messages is not { } messages
        )
        {
            return;
        }

        // Vanilla ignores gears used on a static translocator (it repairs them instead).
        if (playerEntity.World.BlockAccessor.GetBlock(blockSel.Position) is BlockStaticTranslocator)
        {
            return;
        }

        int respawnUses = playerEntity.World.Config.GetAsInt("temporalGearRespawnUses", -1);
        if (respawnUses == 0)
        {
            // World forbids setting the respawn point with gears; vanilla did not consume the gear.
            return;
        }

        RebirthConfig config = TemporalRebirthModSystem.Config;
        int lives = config.LivesPerGear < 0 ? Math.Max(respawnUses, 1) : config.LivesPerGear;
        int left = LifeRules.GetLivesLeft(player);
        if (config.LivesMax > 0)
        {
            lives = Math.Min(lives, config.LivesMax - left);
            if (lives <= 0 && config.LivesPerGear != 0)
            {
                messages.Tell(player, "LivesFull", ("lives", left));
            }
        }

        if (lives > 0)
        {
            LifeRules.AddLives(player, lives);
            messages.Tell(player, "GearLives", ("count", lives), ("lives", LifeRules.GetLivesLeft(player)));
        }
    }
}
