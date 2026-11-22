using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: a ghost is a floating teal light with sparks. The game hides the bodies of spectators itself, so nothing
/// shows who the ghost is.
/// </summary>
internal class WispLook(ICoreClientAPI api)
{
    private const double ViewDistance = 96;
    private const float Glow = 7f;

    private static readonly int CoreColor = ColorUtil.ToRgba(150, 120, 255, 230);
    private static readonly int SparkColor = ColorUtil.ToRgba(220, 90, 235, 215);

    private readonly Dictionary<long, WispLight> lights = new();

    /// <summary>Alpha and size fade over the life of a particle.</summary>
    private readonly SimpleParticleProperties core = new(1, 2, CoreColor, new Vec3d(), new Vec3d(), new Vec3f(), new Vec3f(), 0.35f, 0f, 0.5f, 0.9f, EnumParticleModel.Quad)
    {
        VertexFlags = 255,
        WithTerrainCollision = false,
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -150),
        SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.4f),
    };

    private readonly SimpleParticleProperties sparks = new(0, 1, SparkColor, new Vec3d(), new Vec3d(), new Vec3f(-0.3f, -0.1f, -0.3f), new Vec3f(0.3f, 0.5f, 0.3f), 0.9f, 0.04f, 0.08f, 0.18f, EnumParticleModel.Quad)
    {
        VertexFlags = 255,
        WithTerrainCollision = false,
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -220),
    };

    public void Start()
    {
        api.Event.RegisterGameTickListener(_ => Update(), 33);
        api.Event.LeaveWorld += Clear;
    }

    private void Update()
    {
        if (api.World.Player?.Entity is not { } self)
        {
            return;
        }

        var seen = new HashSet<long>();
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player.Entity is { } entity && LookOf(player) != GhostVisible.Nobody && IsInView(entity, self))
            {
                seen.Add(entity.EntityId);
                Show(entity);
            }
        }

        foreach (long id in lights.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            api.Render.RemovePointLight(lights[id]);
            lights.Remove(id);
        }
    }

    internal static GhostVisible LookOf(IPlayer player) =>
        player.WorldData?.CurrentGameMode == EnumGameMode.Spectator && Enum.TryParse(player.Entity.WatchedAttributes.GetString(ClientState.GhostLook), out GhostVisible look)
            ? look
            : GhostVisible.Nobody;

    /// <summary>Own light would only blind in first person.</summary>
    private bool IsInView(EntityPlayer entity, EntityPlayer self) =>
        entity == self ? api.World.Player.CameraMode != EnumCameraMode.FirstPerson : entity.Pos.DistanceTo(self.Pos) < ViewDistance;

    private void Show(EntityPlayer entity)
    {
        // Bobs slowly; the id keeps several wisps out of step.
        double phase = (api.World.ElapsedMilliseconds / 1000.0) + (entity.EntityId % 100);
        Vec3d pos = entity.Pos.XYZ.Add(0, (entity.LocalEyePos?.Y ?? 1.6) * 0.75 + (0.12 * Math.Sin(phase * 1.7)), 0);

        if (!lights.TryGetValue(entity.EntityId, out WispLight? light))
        {
            lights[entity.EntityId] = light = new WispLight();
            api.Render.AddPointLight(light);
        }

        float flicker = Glow * (0.9f + (0.1f * (float)Math.Sin(phase * 5.3)));
        light.Pos.Set(pos);
        light.Color.Set(0.82f * flicker, 0.9f * flicker, 0.3f * flicker);

        core.MinPos.Set(pos.X - 0.05, pos.Y - 0.05, pos.Z - 0.05);
        core.AddPos.Set(0.1, 0.1, 0.1);
        api.World.SpawnParticles(core);

        sparks.MinPos.Set(pos.X - 0.15, pos.Y - 0.15, pos.Z - 0.15);
        sparks.AddPos.Set(0.3, 0.3, 0.3);
        api.World.SpawnParticles(sparks);
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
