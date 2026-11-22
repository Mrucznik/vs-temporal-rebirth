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
        claims.Any(c => c.PositionInside(pos)) ? 0 : Math.Sqrt(claims.SelectMany(c => c.Areas).Min(a => DistanceSq(a, pos)));

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
