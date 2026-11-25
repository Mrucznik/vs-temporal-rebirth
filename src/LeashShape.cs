using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Where a player out of lives may be: around a place, or in the land claims of a zone. The client stops the player at its
/// edge and the server checks for being far outside, so both sides use this.
/// </summary>
internal abstract class LeashShape
{
    /// <summary>
    /// How far outside (in blocks) the server takes a player for escaped rather than for a delay or a rounding error.
    /// Farther out the client leaves the player alone: the server is about to move them.
    /// </summary>
    internal const double Slack = 8;

    /// <summary>0 inside.</summary>
    public abstract double DistanceOutside(Vec3d pos);

    /// <summary>
    /// The end of a step from <paramref name="from"/>, stopped at the edge. Who is a little outside may only come closer.
    /// With <paramref name="level"/> a walking player slides along the edge at their height.
    /// </summary>
    public abstract Vec3d Stop(Vec3d from, Vec3d to, bool level);

    /// <summary>The point of the edge nearest to <paramref name="from"/> (inside), with the edge's outward normal there.</summary>
    public abstract (Vec3d Point, Vec3d Normal) NearestEdge(Vec3d from);
}

internal sealed class LeashSphere(Vec3d center, double radius) : LeashShape
{
    public Vec3d Center => center;

    public double Radius => radius;

    public override double DistanceOutside(Vec3d pos) => Math.Max(0, pos.DistanceTo(center) - radius);

    public override Vec3d Stop(Vec3d from, Vec3d to, bool level)
    {
        double limit = Math.Max(radius, from.DistanceTo(center));
        double dx = to.X - center.X, dy = to.Y - center.Y, dz = to.Z - center.Z;
        double dist = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (dist <= limit)
        {
            return to;
        }

        double flat = Math.Sqrt((dx * dx) + (dz * dz));
        if (level && Math.Abs(dy) < limit && flat > 0)
        {
            double scale = Math.Sqrt((limit * limit) - (dy * dy)) / flat;
            return new Vec3d(center.X + (dx * scale), to.Y, center.Z + (dz * scale));
        }

        return center.AddCopy(dx / dist * limit, dy / dist * limit, dz / dist * limit);
    }

    public override (Vec3d Point, Vec3d Normal) NearestEdge(Vec3d from)
    {
        Vec3d off = from - center;
        double length = off.Length();
        Vec3d normal = length < 1e-6 ? new Vec3d(1, 0, 0) : off.Clone().Mul(1 / length);
        return (center + (normal * radius), normal);
    }

    /// <summary>For <see cref="ClientState.Leash"/>.</summary>
    public override string ToString() =>
        string.Join(' ', new[] { center.X, center.Y, center.Z, radius }.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));

    public static LeashSphere? Parse(string text)
    {
        string[] parts = text.Split(' ');
        double[] n = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (parts.Length != 4 || !double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]))
            {
                return null;
            }
        }

        return new LeashSphere(new Vec3d(n[0], n[1], n[2]), n[3]);
    }
}

internal sealed class LeashZone(List<LandClaim> claims) : LeashShape
{
    /// <summary>The value of <see cref="ClientState.Leash"/>; clients know the claims and <see cref="ClientState.ZoneGroup"/>.</summary>
    internal const string Name = "zone";

    public override double DistanceOutside(Vec3d pos) =>
        Math.Sqrt(NearestSq(pos));

    /// <summary>The boxes of the claims, in block coordinates.</summary>
    public IEnumerable<Cuboidi> Areas => claims.SelectMany(c => c.Areas);

    /// <summary>
    /// Looks along the six axes and the twenty diagonals for where the zone ends, as the drawn wall does, and takes the
    /// nearest. Good enough for how the wall looks near the player.
    /// </summary>
    public override (Vec3d Point, Vec3d Normal) NearestEdge(Vec3d from)
    {
        List<Cuboidi> areas = Areas.ToList();
        (Vec3d Point, Vec3d Normal) best = (from, new Vec3d(0, 1, 0));
        double bestT = double.MaxValue;
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && y == 0 && z == 0)
            {
                continue;
            }

            var dir = new Vec3d(x, y, z);
            dir.Mul(1 / dir.Length());
            if (Exit(areas, from, dir) is ({ } t, { } normal) && t < bestT)
            {
                bestT = t;
                best = (from + (dir * t), normal);
            }
        }

        return best;
    }

    /// <summary>Where a ray from inside leaves the union of the boxes, and through which face.</summary>
    private static (double T, Vec3d Normal)? Exit(List<Cuboidi> areas, Vec3d from, Vec3d dir)
    {
        double t = 0;
        Vec3d? normal = null;
        for (int hop = 0; hop < 8; hop++)
        {
            Vec3d at = from + (dir * t);
            double best = t;
            Vec3d? bestNormal = null;
            foreach (Cuboidi a in areas)
            {
                if (at.X < a.MinX - 0.02 || at.X > a.MaxX + 0.02 || at.Y < a.MinY - 0.02 || at.Y > a.MaxY + 0.02 || at.Z < a.MinZ - 0.02 || at.Z > a.MaxZ + 0.02)
                {
                    continue;
                }

                double tx = Far(a.MinX, a.MaxX, from.X, dir.X), ty = Far(a.MinY, a.MaxY, from.Y, dir.Y), tz = Far(a.MinZ, a.MaxZ, from.Z, dir.Z);
                double exit = Math.Min(tx, Math.Min(ty, tz));
                if (exit > best)
                {
                    best = exit;
                    bestNormal = exit == tx ? new Vec3d(Math.Sign(dir.X), 0, 0) : exit == ty ? new Vec3d(0, Math.Sign(dir.Y), 0) : new Vec3d(0, 0, Math.Sign(dir.Z));
                }
            }

            if (best <= t + 1e-6)
            {
                break;
            }

            t = best;
            normal = bestNormal;
        }

        return normal == null ? null : (t, normal);
    }

    /// <summary>Along one axis, how far the ray goes before leaving [min, max].</summary>
    private static double Far(double min, double max, double from, double dir) =>
        dir > 0 ? (max - from) / dir : dir < 0 ? (min - from) / dir : double.MaxValue;

    private double NearestSq(Vec3d pos)
    {
        double best = double.MaxValue;
        foreach (LandClaim claim in claims)
        {
            foreach (Cuboidi area in claim.Areas)
            {
                double distance = DistanceSq(area, pos);
                if (distance == 0)
                {
                    return 0;
                }

                best = Math.Min(best, distance);
            }
        }

        return best;
    }

    public override Vec3d Stop(Vec3d from, Vec3d to, bool level)
    {
        double limit = DistanceOutside(from);
        // Without the part of the step that leaves the zone; claims are boxes, so that is one or two of the axes.
        Vec3d[] steps = [to, new(to.X, from.Y, to.Z), new(to.X, to.Y, from.Z), new(from.X, to.Y, to.Z), new(to.X, from.Y, from.Z), new(from.X, from.Y, to.Z), new(from.X, to.Y, from.Z)];
        return steps.FirstOrDefault(s => DistanceOutside(s) <= limit) ?? from;
    }

    private static double DistanceSq(Cuboidi a, Vec3d p)
    {
        double dx = Math.Max(0, Math.Max(a.MinX - p.X, p.X - a.MaxX));
        double dy = Math.Max(0, Math.Max(a.MinY - p.Y, p.Y - a.MaxY));
        double dz = Math.Max(0, Math.Max(a.MinZ - p.Z, p.Z - a.MaxZ));
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
