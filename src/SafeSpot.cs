using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>Finds places where a player does not end up inside a block or fall.</summary>
internal static class SafeSpot
{
    private const int MaxClimb = 32;
    private const int MaxDrop = 256;
    private const int MaxAside = 16;

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

    /// <summary>
    /// The nearest free spot with dry ground under it: straight down (out of a wall first), else around. Null when there is
    /// none. With <paramref name="allowWater"/> the surface of water will do; lava never does.
    /// </summary>
    internal static Vec3d? FindGround(IBlockAccessor blocks, Vec3d pos, bool allowWater = false)
    {
        if (FindInColumn(blocks, pos, MaxDrop, allowWater) is { } here)
        {
            return here;
        }

        Vec3d? best = null;
        for (int r = 1; r <= MaxAside && (best == null || r * r < best.SquareDistanceTo(pos)); r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                // Only the edge of the square; the inside was searched before.
                for (int dz = -r; dz <= r; dz += Math.Abs(dx) == r ? 1 : 2 * r)
                {
                    if (FindInColumn(blocks, pos.AddCopy(dx, 0, dz), MaxClimb, allowWater) is { } spot
                        && !IsNextToLava(blocks, Feet(spot))
                        && (best == null || spot.SquareDistanceTo(pos) < best.SquareDistanceTo(pos)))
                    {
                        best = spot;
                    }
                }
            }
        }

        return best;
    }

    internal static bool IsLoaded(IBlockAccessor blocks, Vec3d pos) => blocks.GetChunkAtBlockPos(Feet(pos)) != null;

    private static Vec3d? FindInColumn(IBlockAccessor blocks, Vec3d pos, int maxDrop, bool allowWater)
    {
        if (FindAbove(blocks, pos) is not { } top)
        {
            return null;
        }

        // Up to 1000 columns are searched at once, so one position is moved along instead of making new ones.
        BlockPos at = Feet(top);
        for (int dy = 0; dy <= maxDrop; dy++)
        {
            // At or under the surface of a liquid there is only more of it below.
            if (IsLiquid(blocks, at))
            {
                return null;
            }

            at.Down();
            if (blocks.GetChunkAtBlockPos(at) == null)
            {
                return null;
            }

            bool ground = !IsPassable(blocks, at), water = allowWater && IsLiquid(blocks, at) && !IsLava(blocks, at);
            if (ground || water)
            {
                at.Up(2);
                return IsLiquid(blocks, at) ? null : top.AddCopy(0, -dy, 0);
            }
        }

        return null;
    }

    private static bool IsLiquid(IBlockAccessor blocks, BlockPos pos) => blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid();

    private static bool IsLava(IBlockAccessor blocks, BlockPos pos) => blocks.GetBlock(pos, BlockLayersAccess.Fluid).LiquidCode == "lava";

    /// <summary>Lava flows, so a spot right next to it is not safe for long.</summary>
    private static bool IsNextToLava(IBlockAccessor blocks, BlockPos feet)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (IsLava(blocks, feet.AddCopy(dx, 0, dz)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static BlockPos Feet(Vec3d pos) => pos.AsBlockPos;

    private static bool IsPassable(IBlockAccessor blocks, BlockPos pos)
    {
        Block block = blocks.GetBlock(pos);
        Cuboidf[]? boxes = block.GetCollisionBoxes(blocks, pos);
        return boxes == null || boxes.Length == 0;
    }
}
