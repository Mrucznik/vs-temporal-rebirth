using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>Vanilla rifts ("wyrwy") are not blocks; ModSystemRifts keeps them on both sides.</summary>
internal static class Rifts
{
    internal static bool IsNearActiveRift(IWorldAccessor world, Vec3d pos, double range)
    {
        ModSystemRifts? rifts = world.Api.ModLoader.GetModSystem<ModSystemRifts>();
        if (rifts == null)
        {
            return false;
        }

        double rangeSq = range * range;
        foreach (Rift rift in rifts.riftsById.Values)
        {
            if (rift.GetNowSize(world.Api) > 0f && rift.Position.SquareDistanceTo(pos) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }
}
