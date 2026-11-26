using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace TemporalRebirth.Recorder;

/// <summary>
/// For the client that records the mod's videos: the hotbar and the health and hunger bars above it stay open and keep
/// working (keys 1-0 still pick a slot), they are just not drawn. The minimap is off in that client's settings.
/// Holding Scroll Lock holds the right mouse button in the world: a right button pressed with xdotool on the recording
/// display (Xwayland) frees the pointer instead, so items could never be held there.
/// </summary>
public sealed class RecorderModSystem : ModSystem
{
    private Harmony? harmony;
    private static ICoreClientAPI? capi;
    private static bool holding;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        // Pause starts the particle test (a chat command typed on the recording display did not arrive).
        bool wasDown = false;
        api.Event.RegisterGameTickListener(_ =>
        {
            bool down = api.Input.KeyboardKeyStateRaw[(int)GlKeys.Pause];
            if (down && !wasDown)
            {
                api.Logger.Notification("[trtrecorder] particle test");
                ParticleTest(api);
            }

            wasDown = down;
        }, 50);
        harmony = new Harmony("trtrecorder");
        harmony.Patch(AccessTools.Method(typeof(SystemMouseInWorldInteractions), "UpdatePicking"), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(HoldRight)));
        harmony.Patch(AccessTools.Method(typeof(HudHotbar), nameof(HudHotbar.OnRenderGUI)), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(Skip)));
        harmony.Patch(AccessTools.Method(typeof(HudStatbar), nameof(HudStatbar.OnRenderGUI)), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(Skip)));
    }

    private static bool Skip() => false;

    /// <summary>Which particle settings show at all: four columns 3 blocks ahead, left to right, for three seconds.</summary>
    private static void ParticleTest(ICoreClientAPI api)
    {
        EntityPlayer self = api.World.Player.Entity;
        Vec3f view = EntityPos.GetViewVector(0, self.Pos.Yaw) * -1;
        var ahead = self.Pos.XYZ.Add(view.X * 3, self.LocalEyePos.Y, view.Z * 3);
        var side = new Vec3d(-view.Z, 0, view.X);
        int rust = ColorUtil.ToRgba(230, 30, 80, 190), wisp = ColorUtil.ToRgba(150, 150, 40, 20);
        SimpleParticleProperties Make(int col, Vec3d at, Vec3f vel, float size) => new(1, 1, col, at, at, vel, vel, 0.7f, 0f, size, size * 1.5f, EnumParticleModel.Quad)
        {
            VertexFlags = 200,
            WithTerrainCollision = false,
        };
        int ticks = 0;
        long id = 0;
        id = api.Event.RegisterGameTickListener(_ =>
        {
            for (int i = 0; i < 4; i++)
            {
                Vec3d at = ahead + (side * ((i - 1.5) * 1.2));
                var moving = new Vec3f(0, -0.6f, 0);
                SimpleParticleProperties props = i switch
                {
                    0 => Make(rust, at, moving, 0.4f),
                    1 => Make(rust, at, new Vec3f(), 0.4f),
                    2 => Make(wisp, at, new Vec3f(), 0.4f),
                    _ => Make(ColorUtil.ToRgba(255, 255, 255, 255), at, new Vec3f(), 0.4f),
                };
                api.World.SpawnParticles(props);
            }

            if (++ticks > 90)
            {
                api.Event.UnregisterGameTickListener(id);
            }
        }, 33);
    }

    /// <summary>Every frame the world is picked: the key stands in for the button, and lets go of it once when released.</summary>
    private static void HoldRight(SystemMouseInWorldInteractions __instance)
    {
        if (capi == null || Traverse.Create(__instance).Field("game").GetValue() is not ClientMain game)
        {
            return;
        }

        bool down = capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ScrollLock];
        if (down)
        {
            game.InWorldMouseState.Right = true;
            holding = true;
        }
        else if (holding)
        {
            game.InWorldMouseState.Right = false;
            holding = false;
        }
    }

    public override void Dispose() => harmony?.UnpatchAll(harmony.Id);
}
