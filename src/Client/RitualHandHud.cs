using Vintagestory.API.Client;

namespace TemporalRebirth;

/// <summary>
/// Client side: a line under the crosshair at a rift where the bare hand can bring a ghost back. The game shows help only
/// for a held item, and here nothing has to be held.
/// </summary>
internal class RitualHandHud(ICoreClientAPI api) : HudElement(api)
{
    private string? shown;

    public override bool Focusable => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    public override bool ShouldReceiveMouseEvents() => false;

    /// <summary>Null hides it.</summary>
    public void Show(string? text)
    {
        if (text == shown)
        {
            return;
        }

        shown = text;
        if (text == null)
        {
            if (IsOpened())
            {
                TryClose();
            }

            return;
        }

        ElementBounds line = ElementBounds.Fixed(0, 0, 500, 24);
        ElementBounds dialog = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 90, 500, 24);
        SingleComposer = capi.Gui.CreateCompo("temporalrebirth-handhint", dialog)
            .AddStaticText(text, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), line)
            .Compose();
        if (!IsOpened())
        {
            TryOpen(false);
        }
    }
}
