using Vintagestory.API.Client;

namespace TemporalRebirth;

/// <summary>
/// Client side: a thin rust bar under the crosshair while a ritual is held. Vanilla shows nothing for how long a gear
/// has been held, and a ritual that ends a breath too early does nothing.
/// </summary>
internal class RitualProgressHud(ICoreClientAPI api) : HudElement(api)
{
    private const double Width = 160, Height = 8;
    private static readonly double[] Rust = [0.75, 0.33, 0.12, 0.9];

    public override bool Focusable => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    public override bool ShouldReceiveMouseEvents() => false;

    public void Show()
    {
        if (SingleComposer == null)
        {
            ElementBounds bar = ElementBounds.Fixed(0, 0, Width, Height);
            ElementBounds dialog = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 60, Width, Height);
            SingleComposer = capi.Gui.CreateCompo("temporalrebirth-ritualprogress", dialog).AddStatbar(bar, Rust, "bar").Compose();
        }

        Set(0);
        if (!IsOpened())
        {
            TryOpen(false);
        }
    }

    public void Set(float done) => SingleComposer?.GetStatbar("bar")?.SetValues(done * 100, 0, 100);

    public void Hide()
    {
        if (IsOpened())
        {
            TryClose();
        }
    }
}
