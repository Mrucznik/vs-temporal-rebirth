using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        harmony = new Harmony("trtrecorder");
        harmony.Patch(AccessTools.Method(typeof(SystemMouseInWorldInteractions), "UpdatePicking"), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(HoldRight)));
        harmony.Patch(AccessTools.Method(typeof(HudHotbar), nameof(HudHotbar.OnRenderGUI)), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(Skip)));
        harmony.Patch(AccessTools.Method(typeof(HudStatbar), nameof(HudStatbar.OnRenderGUI)), prefix: new HarmonyMethod(typeof(RecorderModSystem), nameof(Skip)));
    }

    private static bool Skip() => false;

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
