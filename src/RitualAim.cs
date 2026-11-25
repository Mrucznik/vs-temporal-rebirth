using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Which ghost a caster looks at. Ghosts are spectators the game will not let anyone target, so both sides work it out
/// the same way from the eyes and the look: the client to let a hold begin in the air, the server to decide whom it brings.
/// </summary>
internal static class RitualAim
{
    /// <summary>How far beside the line of sight a ghost still counts as looked at, in blocks: a wisp is small.</summary>
    internal const double Reach = 1.2;

    /// <summary>Ghosts float; the middle of what is drawn is about this high above their feet.</summary>
    internal const double Middle = 0.9;

    /// <summary>The point of a ghost that is aimed at.</summary>
    internal static Vec3d PointOf(Entity ghost) => ghost.Pos.XYZ.Add(0, Middle, 0);

    internal static Vec3d Eyes(EntityPlayer player) => player.Pos.XYZ.Add(0, player.LocalEyePos.Y, 0);

    internal static Vec3d Look(EntityPlayer player)
    {
        Vec3f view = EntityPos.GetViewVector(player.Pos.Pitch, player.Pos.Yaw);
        return new Vec3d(view.X, view.Y, view.Z).Normalize();
    }

    /// <summary>The index of the point closest to the line of sight, within reach and range; -1 for none.</summary>
    internal static int Pick(Vec3d eyes, Vec3d look, IReadOnlyList<Vec3d> points, double range)
    {
        int best = -1;
        double bestOff = Reach;
        for (int i = 0; i < points.Count; i++)
        {
            Vec3d to = points[i] - eyes;
            double along = to.Dot(look);
            if (along <= 0 || along > range + Reach)
            {
                continue;
            }

            double off = (to - (look * along)).Length();
            if (off < bestOff)
            {
                bestOff = off;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The yaw (as the game counts it) of someone at <paramref name="from"/> facing <paramref name="to"/>.</summary>
    internal static float YawTowards(Vec3d from, Vec3d to) => (float)Math.Atan2(-(to.X - from.X), -(to.Z - from.Z));
}
