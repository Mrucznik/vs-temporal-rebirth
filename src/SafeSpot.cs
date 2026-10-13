using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Finds a place where a solid (non no-clip) ghost can float: air at feet and head height.
/// Needed because ghosts keep collisions and could otherwise be placed inside terrain.
/// </summary>
internal static class SafeSpot
{
    private const int MaxClimb = 32;

    internal static bool IsFree(IBlockAccessor blocks, Vec3d pos)
    {
        var feet = new BlockPos((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));
        return IsPassable(blocks, feet) && IsPassable(blocks, feet.UpCopy());
    }

    /// <summary>
    /// Returns <paramref name="pos"/> if free, otherwise the first free spot straight above it,
    /// or null when there is none within reach (or the chunk is not loaded).
    /// </summary>
    internal static Vec3d? FindAbove(IBlockAccessor blocks, Vec3d pos)
    {
        for (int dy = 0; dy <= MaxClimb; dy++)
        {
            var candidate = new Vec3d(pos.X, Math.Floor(pos.Y) + dy, pos.Z);
            if (blocks.GetChunkAtBlockPos(candidate.AsBlockPos) == null)
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

    private static bool IsPassable(IBlockAccessor blocks, BlockPos pos)
    {
        Block block = blocks.GetBlock(pos);
        Cuboidf[]? boxes = block.GetCollisionBoxes(blocks, pos);
        return boxes == null || boxes.Length == 0;
    }
}
