using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// The particles of a ritual, the same for the caster (drawn by their own client every frame) and for players near them
/// (sent by the server a few times a second). Rust coloured, drawn into the spot the gear is held at.
/// </summary>
internal static class RitualLook
{
    /// <summary>How long vanilla wants the gear held.</summary>
    internal const float HoldSeconds = 3.5f;

    private const float MoteLife = 0.7f;

    // Particle colours are read with red in the low byte, so ToRgba's r and b swap places here.
    private static readonly int Rust = ColorUtil.ToRgba(230, 30, 80, 190);
    private static readonly int Ember = ColorUtil.ToRgba(255, 60, 150, 240);

    /// <summary>One mote flying from around the spot into it. Later in the hold they come from closer and glow more.</summary>
    internal static SimpleParticleProperties Mote(Random rand, Vec3d center, float progress)
    {
        double radius = 2.2 - progress;
        double yaw = rand.NextDouble() * Math.PI * 2;
        // Mostly from above and the sides: from below they would start inside the ground.
        double pitch = (rand.NextDouble() * 0.9) + 0.1;
        var from = new Vec3d(
            center.X + (Math.Cos(yaw) * Math.Cos(pitch) * radius),
            center.Y + (Math.Sin(pitch) * radius),
            center.Z + (Math.Sin(yaw) * Math.Cos(pitch) * radius));
        var velocity = new Vec3f((float)((center.X - from.X) / MoteLife), (float)((center.Y - from.Y) / MoteLife), (float)((center.Z - from.Z) / MoteLife));
        return new SimpleParticleProperties(1, 1, rand.NextDouble() < 0.25 ? Ember : Rust, from, from, velocity, velocity, MoteLife, 0f, 0.12f + (0.12f * progress), 0.22f + (0.16f * progress), EnumParticleModel.Quad)
        {
            VertexFlags = (int)(80 + (175 * progress)),
            WithTerrainCollision = false,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.15f),
        };
    }

    /// <summary>The flash when someone is brought back.</summary>
    internal static SimpleParticleProperties Burst(Vec3d center) =>
        new(60, 120, Rust, center.AddCopy(-0.3, -0.3, -0.3), center.AddCopy(0.3, 0.3, 0.3), new Vec3f(-2.5f, -1f, -2.5f), new Vec3f(2.5f, 3.5f, 2.5f), 1.4f, 0.2f, 0.15f, 0.45f, EnumParticleModel.Quad)
        {
            VertexFlags = 200,
            WithTerrainCollision = false,
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -120),
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.2f),
        };
}
