using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: the sphere that holds the player, drawn a little beyond it so the camera always stops in front of it. Nothing within 50 blocks of the edge;
/// then dark contour lines with small turning gears, rift smoke and bolts that grow as the ghost comes near, less
/// see-through from about 4 blocks, with a glow running out from where it is hit. Zones have no walls yet.
/// </summary>
internal class LeashLook : IRenderer
{
    /// <summary>Same distance as where the shader starts to show anything.</summary>
    private const double VisibleAt = 50;
    private const float GlowSeconds = 2.5f;

    private static readonly string[] HitNames = ["hit0", "hit1", "hit2"];

    private readonly ICoreClientAPI api;
    private readonly LeashGuard guard;
    private readonly SceneShader shader;
    private readonly (Vec3d At, long Ms)[] hits = new (Vec3d, long)[HitNames.Length];
    private int nextHit;

    public LeashLook(ICoreClientAPI api, LeashGuard guard)
    {
        this.api = api;
        this.guard = guard;
        shader = new SceneShader(api, "leash");
        guard.Hit += OnHit;
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "temporalrebirth-leash");
    }

    public double RenderOrder => 0.06;

    public int RenderRange => 9999;

    private void OnHit(Vec3d at)
    {
        hits[nextHit] = (at, api.InWorldEllapsedMilliseconds);
        nextHit = (nextHit + 1) % hits.Length;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!shader.Works || guard.Shape is not LeashSphere sphere || api.World.Player?.Entity is not { } player)
        {
            return;
        }

        Vec3d origin = player.CameraPos;
        Vec3d middle = origin.AddCopy(0, player.LocalEyePos.Y * 0.6, 0);
        if (Math.Abs(sphere.Radius - middle.DistanceTo(sphere.Center)) > VisibleAt)
        {
            return;
        }

        IShaderProgram p = shader.Begin();
        // Relative to the camera and in doubles: the sphere can be huge and far from the middle of the world.
        double x = origin.X + shader.Camera.X - sphere.Center.X, y = origin.Y + shader.Camera.Y - sphere.Center.Y, z = origin.Z + shader.Camera.Z - sphere.Center.Z;
        double dist = Math.Sqrt((x * x) + (y * y) + (z * z));
        p.Uniform("fromCenter", (float)x, (float)y, (float)z);
        p.Uniform("outside", (float)((dist - sphere.Radius) * (dist + sphere.Radius)));
        p.Uniform("player", Relative(middle, origin));
        for (int i = 0; i < hits.Length; i++)
        {
            float age = hits[i].At == null ? -1 : (api.InWorldEllapsedMilliseconds - hits[i].Ms) / 1000f;
            Vec3f at = age is >= 0 and < GlowSeconds ? Relative(hits[i].At, origin) : new Vec3f();
            p.Uniform(HitNames[i], at.X, at.Y, at.Z, age is >= 0 and < GlowSeconds ? age : -1);
        }

        shader.Draw();
        shader.End();
    }

    private Vec3f Relative(Vec3d pos, Vec3d origin) =>
        new((float)(pos.X - origin.X) - shader.Camera.X, (float)(pos.Y - origin.Y) - shader.Camera.Y, (float)(pos.Z - origin.Z) - shader.Camera.Z);

    public void Dispose() => shader.Dispose();
}
