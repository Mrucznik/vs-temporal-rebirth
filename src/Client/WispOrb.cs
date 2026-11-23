using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: the bright core of a wisp, drawn by a shader. Particles alone are not enough: a player can turn them off
/// (or have them off without knowing), and then a wisp was only a patch of light on the ground.
/// </summary>
internal sealed class WispOrb : IRenderer
{
    private const double ViewDistance = 96;
    private const float Radius = 0.45f;

    private readonly ICoreClientAPI api;
    private readonly Ghosts ghosts;
    private readonly SceneShader shader;

    public WispOrb(ICoreClientAPI api, Ghosts ghosts)
    {
        this.api = api;
        this.ghosts = ghosts;
        shader = new SceneShader(api, "wisp");
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "temporalrebirth-wisp");
    }

    public double RenderOrder => 0.065;

    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!shader.Works || ghosts.Visible.Count == 0 || api.World.Player?.Entity is not { } self)
        {
            return;
        }

        IShaderProgram? p = null;
        foreach (var (player, _) in ghosts.Visible)
        {
            if (player.Entity is not { } ghost || (ghost == self && api.World.Player.CameraMode == EnumCameraMode.FirstPerson) || ghost.Pos.DistanceTo(self.Pos) > ViewDistance)
            {
                continue;
            }

            p ??= shader.Begin();
            Vec3d at = WispLook.Center(api, ghost);
            var feet = new Vec3f((float)(at.X - self.CameraPos.X), (float)(at.Y - Radius - self.CameraPos.Y), (float)(at.Z - self.CameraPos.Z));
            float dx = shader.Camera.X - feet.X, dz = shader.Camera.Z - feet.Z;
            float length = Math.Max(0.001f, MathF.Sqrt((dx * dx) + (dz * dz)));
            p.Uniform("feet", feet);
            p.Uniform("side", dz / length * Radius, 0f, -dx / length * Radius);
            p.Uniform("height", Radius * 2);
            p.Uniform("seed", ghost.EntityId % 97 * 1f);
            shader.Draw();
        }

        if (p != null)
        {
            shader.End();
        }
    }

    public void Dispose() => shader.Dispose();
}
