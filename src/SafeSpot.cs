using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>Finds places where a player does not end up inside a block or fall.</summary>
internal static class SafeSpot
{
    private const int MaxClimb = 32;
    private const int MaxDrop = 256;

    internal static bool IsFree(IBlockAccessor blocks, Vec3d pos)
    {
        var feet = Feet(pos);
        return IsPassable(blocks, feet) && IsPassable(blocks, feet.UpCopy());
    }

    /// <summary>
    /// Returns <paramref name="pos"/> if free, otherwise the first free spot straight above it,
    /// or null when there is none within reach (or the chunk is not loaded). For ghosts: may be in the air.
    /// </summary>
    internal static Vec3d? FindAbove(IBlockAccessor blocks, Vec3d pos)
    {
        for (int dy = 0; dy <= MaxClimb; dy++)
        {
            var candidate = new Vec3d(pos.X, Math.Floor(pos.Y) + dy, pos.Z);
            if (blocks.GetChunkAtBlockPos(Feet(candidate)) == null)
            {
                return null;
            }

            if (IsFree(blocks, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The nearest free spot with ground under it: out of a wall upwards, then down to the floor.</summary>
    internal static Vec3d? FindGround(IBlockAccessor blocks, Vec3d pos)
    {
        Vec3d? spot = FindAbove(blocks, pos);
        for (int dy = 0; spot != null && dy <= MaxDrop; dy++)
        {
            var below = Feet(spot).DownCopy();
            if (blocks.GetChunkAtBlockPos(below) == null)
            {
                return null;
            }

            if (!IsPassable(blocks, below) || blocks.GetBlock(below, BlockLayersAccess.Fluid).IsLiquid())
            {
                return spot;
            }

            spot = spot.AddCopy(0, -1, 0);
        }

        return null;
    }

    private static BlockPos Feet(Vec3d pos) => pos.AsBlockPos;

    private static bool IsPassable(IBlockAccessor blocks, BlockPos pos)
    {
        Block block = blocks.GetBlock(pos);
        Cuboidf[]? boxes = block.GetCollisionBoxes(blocks, pos);
        return boxes == null || boxes.Length == 0;
    }
}
