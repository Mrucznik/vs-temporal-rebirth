using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// Client side: who holds a temporal gear sees the echo of a ghost, a wavering silhouette that bends the world behind it,
/// with its name. Ghosts are wisps for everyone else, and for everyone if the shader does not work.
/// </summary>
internal class EchoLook : IRenderer
{
    private const double ViewDistance = 48, NameDistance = 24;
    private const float HalfWidth = 0.7f, Height = 2.1f;

    private readonly ICoreClientAPI api;
    private readonly SceneShader shader;
    private readonly Dictionary<string, LoadedTexture> names = new();

    public EchoLook(ICoreClientAPI api)
    {
        this.api = api;
        shader = new SceneShader(api, "echo");
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "temporalrebirth-echo");
        api.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "temporalrebirth-echonames");
    }

    public double RenderOrder => 0.07;

    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!shader.Works || api.World.Player?.Entity is not { } self || !HoldsGear(self))
        {
            return;
        }

        List<IPlayer> echoes = api.World.AllOnlinePlayers.Where(p => IsEcho(p, self)).ToList();
        if (echoes.Count == 0)
        {
            return;
        }

        if (stage == EnumRenderStage.AfterBlit)
        {
            DrawBodies(echoes, self);
        }
        else
        {
            DrawNames(echoes, self);
        }
    }

    private static bool HoldsGear(EntityPlayer self) =>
        self.RightHandItemSlot?.Itemstack?.Collectible is ItemTemporalGear || self.LeftHandItemSlot?.Itemstack?.Collectible is ItemTemporalGear;

    private static bool IsEcho(IPlayer player, EntityPlayer self) =>
        player.Entity is { } entity && entity != self && WispLook.LookOf(player) == GhostVisible.WispAndEcho && entity.Pos.DistanceTo(self.Pos) < ViewDistance;

    private void DrawBodies(List<IPlayer> echoes, EntityPlayer self)
    {
        IShaderProgram p = shader.Begin();
        foreach (EntityPlayer ghost in echoes.Select(e => e.Entity).OrderByDescending(e => e.Pos.DistanceTo(self.Pos)))
        {
            var feet = new Vec3f((float)(ghost.Pos.X - self.CameraPos.X), (float)(ghost.Pos.InternalY - self.CameraPos.Y), (float)(ghost.Pos.Z - self.CameraPos.Z));
            // Turned to the viewer, but standing upright.
            float dx = shader.Camera.X - feet.X, dz = shader.Camera.Z - feet.Z;
            float length = Math.Max(0.001f, MathF.Sqrt((dx * dx) + (dz * dz)));
            p.Uniform("feet", feet);
            p.Uniform("side", dz / length * HalfWidth, 0f, -dx / length * HalfWidth);
            p.Uniform("height", Height);
            p.Uniform("seed", ghost.EntityId % 97 * 1f);
            shader.Draw();
        }

        shader.End();
    }

    private void DrawNames(List<IPlayer> echoes, EntityPlayer self)
    {
        IRenderAPI render = api.Render;
        foreach (IPlayer echo in echoes.Where(e => e.Entity.Pos.DistanceTo(self.Pos) < NameDistance))
        {
            EntityPos pos = echo.Entity.Pos;
            Vec3d at = MatrixToolsd.Project(new Vec3d(pos.X, pos.InternalY + Height + 0.15, pos.Z), render.PerspectiveProjectionMat, render.PerspectiveViewMat, render.FrameWidth, render.FrameHeight);
            if (at.Z < 0)
            {
                continue;
            }

            LoadedTexture texture = NameOf(echo.PlayerName);
            float scale = Math.Min(1f, 4f / Math.Max(1f, (float)at.Z));
            render.Render2DTexture(texture.TextureId, (float)at.X - (scale * texture.Width / 2f), render.FrameHeight - (float)at.Y - (scale * texture.Height), scale * texture.Width, scale * texture.Height, 20f);
        }
    }

    private LoadedTexture NameOf(string name)
    {
        if (!names.TryGetValue(name, out LoadedTexture? texture))
        {
            CairoFont font = CairoFont.WhiteMediumText().WithColor([0.62, 0.97, 0.92, 0.95]).WithStroke([0, 0, 0, 0.7], 2);
            names[name] = texture = api.Gui.TextTexture.GenUnscaledTextTexture(name, font);
        }

        return texture;
    }

    public void Dispose()
    {
        shader.Dispose();
        foreach (LoadedTexture texture in names.Values)
        {
            texture.Dispose();
        }

        names.Clear();
    }
}
