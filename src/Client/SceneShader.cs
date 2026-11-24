using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// A shader of this mod, drawn over the finished picture of the world: it can bend what is behind it and knows how far away
/// that is. Without the shader (it did not compile) nothing is drawn.
/// </summary>
internal sealed class SceneShader : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly string name;
    private readonly MeshRef quad;
    private readonly float[] viewProjection = Mat4f.Create(), inverse = Mat4f.Create(), inverseView = Mat4f.Create();
    private IShaderProgram? program;

    public SceneShader(ICoreClientAPI api, string name)
    {
        this.api = api;
        this.name = name;
        quad = api.Render.UploadMesh(QuadMeshUtil.GetQuad());
        api.Event.ReloadShader += Load;
        Load();
    }

    public bool Works => program != null;

    /// <summary>The camera, seen from where the game puts the origin: the player's CameraPos.</summary>
    public Vec3f Camera { get; } = new();

    private bool Load()
    {
        program = null;
        try
        {
            IShaderProgram made = api.Shader.NewShaderProgram();
            made.AssetDomain = "temporalrebirth";
            made.VertexShader = api.Shader.NewShader(EnumShaderType.VertexShader);
            made.FragmentShader = api.Shader.NewShader(EnumShaderType.FragmentShader);
            api.Shader.RegisterFileShaderProgram(name, made);
            if (made.Compile())
            {
                program = made;
            }
        }
        catch (Exception e)
        {
            api.Logger.ModError("Shader {0}: {1}", name, e.Message);
        }

        if (program == null)
        {
            api.Logger.ModError("The shader {0} does not work here; what it shows stays invisible.", name);
        }

        // The game itself does not depend on it.
        return true;
    }

    /// <summary>Starts drawing; every <see cref="Draw"/> until <see cref="End"/> is one quad.</summary>
    public IShaderProgram Begin()
    {
        Mat4f.Mul(viewProjection, api.Render.CurrentProjectionMatrix, api.Render.CameraMatrixOriginf);
        Mat4f.Invert(inverse, viewProjection);
        Mat4f.Invert(inverseView, api.Render.CameraMatrixOriginf);
        Camera.Set(inverseView[12], inverseView[13], inverseView[14]);

        IShaderProgram p = program!;
        api.Render.GLDepthMask(false);
        api.Render.GlToggleBlend(true);
        p.Use();
        p.BindTexture2D("sceneColor", api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0], 0);
        p.BindTexture2D("sceneDepth", api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 1);
        // Setting a uniform the shader does not have throws.
        if (p.HasUniform("viewProjection"))
        {
            p.UniformMatrix("viewProjection", viewProjection);
        }

        p.UniformMatrix("inverseViewProjection", inverse);
        p.Uniform("camera", Camera);
        p.Uniform("time", (float)(api.InWorldEllapsedMilliseconds % 3_600_000) / 1000f);
        return p;
    }

    public void Draw() => api.Render.RenderMesh(quad);

    public void End()
    {
        program!.Stop();
        api.Render.GLDepthMask(true);
    }

    public void Dispose()
    {
        api.Event.ReloadShader -= Load;
        quad.Dispose();
    }
}
