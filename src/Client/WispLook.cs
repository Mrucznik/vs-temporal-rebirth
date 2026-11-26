using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: a ghost is a floating light in the rift's dark rust, with sparks. The game hides the bodies of spectators itself, so nothing
/// shows who the ghost is.
/// </summary>
internal class WispLook(ICoreClientAPI api, Ghosts ghosts)
{
    private const double ViewDistance = 96;

    /// <summary>The game only draws a few dynamic lights, nobody needs those of far wisps.</summary>
    private const double LightDistance = 40;

    /// <summary>Farther wisps get a third of the particles.</summary>
    private const double DetailDistance = 32;
    private const float Glow = 7f;

    // A dark, dying ember rather than a flame: the bright core is the shader's (WispOrb); what is left behind is smoke.
    private static readonly int CoreColor = ColorUtil.ToRgba(120, 70, 12, 8);
    private static readonly int SparkColor = ColorUtil.ToRgba(170, 160, 55, 18);

    private readonly Dictionary<long, WispLight> lights = new();
    private readonly HashSet<long> seen = new();
    private int tick;

    /// <summary>
    /// What the wisp leaves behind: dark rust smoke that barely glows, swells, sinks a little and fades slowly, so a moving
    /// ghost trails a faint stain instead of a bright streak.
    /// </summary>
    private readonly SimpleParticleProperties core = new(0, 1.2f, CoreColor, new Vec3d(), new Vec3d(), new Vec3f(-0.05f, -0.18f, -0.05f), new Vec3f(0.05f, -0.04f, 0.05f), 0.9f, 0f, 0.3f, 0.5f, EnumParticleModel.Quad)
    {
        VertexFlags = 40,
        WithTerrainCollision = false,
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -120),
        SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.5f),
    };

    /// <summary>Now and then a dim ember drops out of it.</summary>
    private readonly SimpleParticleProperties sparks = new(0, 0.35f, SparkColor, new Vec3d(), new Vec3d(), new Vec3f(-0.15f, -0.25f, -0.15f), new Vec3f(0.15f, 0.05f, 0.15f), 1.1f, 0.02f, 0.05f, 0.1f, EnumParticleModel.Quad)
    {
        VertexFlags = 110,
        WithTerrainCollision = false,
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -160),
    };

    public void Start()
    {
        api.Event.RegisterGameTickListener(_ => Update(), 33);
        api.Event.LeaveWorld += Clear;
    }

    private void Update()
    {
        if (api.World.Player?.Entity is not { } self || (ghosts.Visible.Count == 0 && lights.Count == 0))
        {
            return;
        }

        tick++;
        seen.Clear();
        foreach (var (player, _) in ghosts.Visible)
        {
            if (player.Entity is { } entity && IsInView(entity, self))
            {
                double distance = entity.Pos.DistanceTo(self.Pos);
                if (distance < LightDistance)
                {
                    seen.Add(entity.EntityId);
                }

                Show(entity, distance < LightDistance, distance < DetailDistance || tick % 3 == 0);
            }
        }

        if (lights.Count > seen.Count)
        {
            foreach (long id in lights.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                api.Render.RemovePointLight(lights[id]);
                lights.Remove(id);
            }
        }
    }

    /// <summary>Own light would only blind in first person.</summary>
    private bool IsInView(EntityPlayer entity, EntityPlayer self) =>
        entity == self ? api.World.Player.CameraMode != EnumCameraMode.FirstPerson : entity.Pos.DistanceTo(self.Pos) < ViewDistance;

    private void Show(EntityPlayer entity, bool lit, bool particles)
    {
        double phase = Phase(api, entity);
        Vec3d pos = Center(api, entity);

        if (lit)
        {
            Light(entity.EntityId, pos, phase);
        }

        if (!particles)
        {
            return;
        }

        core.MinPos.Set(pos.X - 0.05, pos.Y - 0.05, pos.Z - 0.05);
        core.AddPos.Set(0.1, 0.1, 0.1);
        api.World.SpawnParticles(core);

        sparks.MinPos.Set(pos.X - 0.15, pos.Y - 0.15, pos.Z - 0.15);
        sparks.AddPos.Set(0.3, 0.3, 0.3);
        api.World.SpawnParticles(sparks);
    }

    private static double Phase(ICoreClientAPI api, EntityPlayer entity) => (api.World.ElapsedMilliseconds / 1000.0) + (entity.EntityId % 100);

    /// <summary>Where the wisp floats; it bobs slowly, and the id keeps several wisps out of step. Also used by <see cref="WispOrb"/>.</summary>
    internal static Vec3d Center(ICoreClientAPI api, EntityPlayer entity) =>
        entity.Pos.XYZ.Add(0, (entity.LocalEyePos?.Y ?? 1.6) * 0.75 + (0.12 * Math.Sin(Phase(api, entity) * 1.7)), 0);

    private void Light(long id, Vec3d pos, double phase)
    {
        if (!lights.TryGetValue(id, out WispLight? light))
        {
            lights[id] = light = new WispLight();
            api.Render.AddPointLight(light);
        }

        float flicker = Glow * (0.9f + (0.1f * (float)Math.Sin(phase * 5.3)));
        light.Pos.Set(pos);
        light.Color.Set(0.1f * flicker, 0.22f * flicker, 0.85f * flicker);
    }

    private void Clear()
    {
        foreach (WispLight light in lights.Values)
        {
            api.Render.RemovePointLight(light);
        }

        lights.Clear();
    }

    /// <summary>The game reads the color as blue, green, red.</summary>
    private sealed class WispLight : IPointLight
    {
        public Vec3f Color { get; } = new();

        public Vec3d Pos { get; } = new();
    }
}
