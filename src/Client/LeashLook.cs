using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: the sphere or the land claims that hold the player, drawn a little beyond the edge so the camera always
/// stops in front of it. Nothing farther than 50 blocks from the edge; then dark contour lines with small turning gears,
/// rift smoke and bolts that grow as the ghost comes near, less see-through around the ghost from about 4 blocks, with a
/// glow running out from where it is hit.
/// </summary>
internal class LeashLook : IRenderer
{
    /// <summary>Same distance as where the shader starts to show anything.</summary>
    private const double VisibleAt = 50;
    private const float GlowSeconds = 2.5f;

    /// <summary>How far beyond the edge the walls of a zone are drawn; spheres do this in the shader.</summary>
    private const double BeyondBy = 0.3;

    /// <summary>The shader takes this many claim boxes, the nearest ones.</summary>
    private const int MaxBoxes = 8;

    private readonly float[] boxMin = new float[MaxBoxes * 4], boxMax = new float[MaxBoxes * 4];

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
        if (!shader.Works || guard.Shape is not { } shape || api.World.Player?.Entity is not { } player)
        {
            return;
        }

        Vec3d origin = player.CameraPos;
        Vec3d middle = origin.AddCopy(0, player.LocalEyePos.Y * 0.6, 0);
        (Vec3d edge, Vec3d normal) = shape.NearestEdge(middle);
        double wallDist = edge.DistanceTo(middle);
        if (wallDist > VisibleAt)
        {
            return;
        }

        IShaderProgram p = shader.Begin();
        if (shape is LeashSphere sphere)
        {
            // Relative to the camera and in doubles: the sphere can be huge and far from the middle of the world.
            double x = origin.X + shader.Camera.X - sphere.Center.X, y = origin.Y + shader.Camera.Y - sphere.Center.Y, z = origin.Z + shader.Camera.Z - sphere.Center.Z;
            double dist = Math.Sqrt((x * x) + (y * y) + (z * z));
            p.Uniform("shape", 0);
            p.Uniform("fromCenter", (float)x, (float)y, (float)z);
            p.Uniform("outside", (float)((dist - sphere.Radius) * (dist + sphere.Radius)));
        }
        else if (shape is LeashZone zone)
        {
            List<Cuboidi> areas = zone.Areas.OrderBy(a => Distance(a, middle)).Take(MaxBoxes).ToList();
            for (int i = 0; i < areas.Count; i++)
            {
                Vec3f lo = Relative(new Vec3d(areas[i].MinX - BeyondBy, areas[i].MinY - BeyondBy, areas[i].MinZ - BeyondBy), origin);
                Vec3f hi = Relative(new Vec3d(areas[i].MaxX + BeyondBy, areas[i].MaxY + BeyondBy, areas[i].MaxZ + BeyondBy), origin);
                (boxMin[i * 4], boxMin[(i * 4) + 1], boxMin[(i * 4) + 2]) = (lo.X, lo.Y, lo.Z);
                (boxMax[i * 4], boxMax[(i * 4) + 1], boxMax[(i * 4) + 2]) = (hi.X, hi.Y, hi.Z);
            }

            p.Uniform("shape", 1);
            p.Uniform("boxCount", areas.Count);
            p.Uniforms4("boxMin", MaxBoxes, boxMin);
            p.Uniforms4("boxMax", MaxBoxes, boxMax);
            // A corner that stays where it is while the player moves: the pattern is laid out from it.
            Cuboidi first = zone.Areas.First();
            p.Uniform("anchor", Relative(new Vec3d(first.MinX, first.MinY, first.MinZ), origin));
        }

        p.Uniform("player", Relative(middle, origin));
        p.Uniform("wallPoint", Relative(edge, origin));
        p.Uniform("wallNormal", (float)normal.X, (float)normal.Y, (float)normal.Z);
        p.Uniform("wallDist", (float)wallDist);
        for (int i = 0; i < hits.Length; i++)
        {
            float age = hits[i].At == null ? -1 : (api.InWorldEllapsedMilliseconds - hits[i].Ms) / 1000f;
            Vec3f at = age is >= 0 and < GlowSeconds ? Relative(hits[i].At, origin) : new Vec3f();
            p.Uniform(HitNames[i], at.X, at.Y, at.Z, age is >= 0 and < GlowSeconds ? age : -1);
        }

        shader.Draw();
        shader.End();
    }

    private static double Distance(Cuboidi a, Vec3d p)
    {
        double dx = Math.Max(0, Math.Max(a.MinX - p.X, p.X - a.MaxX));
        double dy = Math.Max(0, Math.Max(a.MinY - p.Y, p.Y - a.MaxY));
        double dz = Math.Max(0, Math.Max(a.MinZ - p.Z, p.Z - a.MaxZ));
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private Vec3f Relative(Vec3d pos, Vec3d origin) =>
        new((float)(pos.X - origin.X) - shader.Camera.X, (float)(pos.Y - origin.Y) - shader.Camera.Y, (float)(pos.Z - origin.Z) - shader.Camera.Z);

    public void Dispose() => shader.Dispose();
}
