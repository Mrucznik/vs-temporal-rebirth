using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth.Patches;

/// <summary>
/// Holding a temporal gear (right click on a block for 3.5 s, the vanilla "set respawn point" use):
///  - at a ritual site: performs the ritual (or opens the chooser) instead of setting the respawn point;
///  - anywhere else: vanilla sets the respawn point and we add extra lives on top.
/// Players out of lives get neither: a gear would be a way out.
/// </summary>
[HarmonyPatch(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStop))]
internal static class TemporalGearPatch
{
    // Same threshold vanilla uses before it consumes the gear.
    private const double VanillaUseSeconds = 3.45;

    /// <summary>Set while the chooser's "bind here" runs vanilla on purpose.</summary>
    [ThreadStatic]
    private static bool binding;

    /// <returns>false to skip vanilla (the gear did something else, or nothing).</returns>
    private static bool Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, out bool __state)
    {
        __state = false;
        if (
            binding
            || byEntity.World.Side != EnumAppSide.Server
            || byEntity is not EntityPlayer playerEntity
            || playerEntity.World.PlayerByUid(playerEntity.PlayerUID) is not IServerPlayer player
            || TemporalRebirthModSystem.Server is not { } server
        )
        {
            return true;
        }

        // A hold aimed at a ghost in the air has no block; vanilla would do nothing with it.
        bool inAir = blockSel == null && server.Rituals.IsAirHold(player);
        server.Rituals.EndHold(player);
        if ((blockSel == null && !inAir) || secondsUsed < VanillaUseSeconds || LifeRules.UnlimitedLives(playerEntity.World))
        {
            return true;
        }

        bool lost = server.Afterlife.Contains(player.PlayerUID);
        if (!lost && !server.Rituals.IsRitualSite(player))
        {
            return true;
        }

        __state = true;
        if (!lost && Rituals.CanPerform(player, server.Afterlife))
        {
            server.Rituals.Complete(player, slot, blockSel?.Position);
        }

        // The client may have predicted the gear being used up; resend the slot either way.
        slot.MarkDirty();
        return false;
    }

    /// <summary>
    /// The chooser's other answer where rituals work anywhere: what the gear would have done without anyone waiting,
    /// the vanilla spawn point and our lives, through the same code. Returns whether the gear was used.
    /// </summary>
    internal static bool BindHere(IServerPlayer player, ItemSlot slot, BlockPos pos)
    {
        if (slot.Itemstack?.Collectible is not ItemTemporalGear gear)
        {
            return false;
        }

        int before = slot.StackSize;
        var selection = new BlockSelection { Position = pos, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
        binding = true;
        try
        {
            gear.OnHeldInteractStop((float)VanillaUseSeconds + 0.05f, slot, player.Entity, selection, null);
        }
        finally
        {
            binding = false;
        }

        slot.MarkDirty();
        return slot.Itemstack == null || slot.StackSize < before;
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
            || TemporalRebirthModSystem.Server is not { } server
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

        RebirthConfig config = server.Config;
        int lives = config.LivesPerGear < 0 ? Math.Max(respawnUses, 1) : config.LivesPerGear;
        int left = LifeRules.GetLivesLeft(player);
        if (config.LivesMax > 0)
        {
            lives = Math.Min(lives, config.LivesMax - left);
            if (lives <= 0 && config.LivesPerGear != 0)
            {
                server.Messages.Tell(player, "LivesFull", ("lives", left));
            }
        }

        if (lives > 0)
        {
            LifeRules.AddLives(player, lives);
            server.Messages.Tell(player, "GearLives", ("count", lives), ("lives", LifeRules.GetLivesLeft(player)));
        }
    }
}
