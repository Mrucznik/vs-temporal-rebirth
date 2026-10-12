using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth.Patches;

/// <summary>
/// Holding a temporal gear (right click on a block for 3.5 s, the vanilla "set respawn point" use):
///  - next to an active rift: performs the revival ritual instead of setting the respawn point;
///  - anywhere else: vanilla sets the respawn point and we add extra lives on top.
/// </summary>
[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStop))]
internal static class TemporalGearPatch
{
    // Same threshold vanilla uses before it consumes the gear.
    private const double VanillaUseSeconds = 3.45;

    /// <returns>false to skip vanilla (ritual took over).</returns>
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
            || !Rifts.IsNearActiveRift(playerEntity.World, playerEntity.Pos.XYZ, TemporalRebirthModSystem.Config.RitualRiftRange)
        )
        {
            return true;
        }

        __state = true;
        if (DownedCompat.IsDowned(playerEntity))
        {
            return false;
        }

        if (TemporalRebirthModSystem.Rebirth!.PerformRitual(player, blockSel.Position))
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

        int perGear = TemporalRebirthModSystem.Config.LivesPerGear;
        int lives = perGear < 0 ? Math.Max(respawnUses, 1) : perGear;
        if (lives <= 0)
        {
            return;
        }

        LifeRules.AddLives(player, lives);
        player.SendLocalisedMessage(
            GlobalConstants.GeneralChatGroup,
            "temporalrebirth:gear-lives",
            lives,
            LifeRules.GetLivesLeft(player)
        );
    }
}
