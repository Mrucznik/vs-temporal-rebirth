using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace TemporalRebirth;

/// <summary>
/// Client side: how long until the player comes back, at the top of the screen. The server sends the seconds left every
/// few seconds and the client counts down in between, so the clock of the player's computer does not matter.
/// </summary>
internal class TimerHud : HudElement
{
    private const double Width = 700, Height = 25;

    /// <summary>Smaller differences are the delay of the network; following them would make the seconds jump.</summary>
    private const long ToleranceMs = 1500;

    private readonly long tickId;
    private int lastSent = -1;
    private long endMs;
    private string shown = "";

    public TimerHud(ICoreClientAPI api)
        : base(api)
    {
        tickId = api.Event.RegisterGameTickListener(_ => Update(), 200);
    }

    public override bool Focusable => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    public override bool ShouldReceiveMouseEvents() => false;

    private void Update()
    {
        var attrs = capi.World.Player?.Entity?.WatchedAttributes;
        int sent = attrs?.GetInt(ClientState.TimerSeconds, -1) ?? -1;
        if (sent < 0)
        {
            lastSent = -1;
            if (IsOpened())
            {
                TryClose();
            }

            return;
        }

        long now = capi.World.ElapsedMilliseconds;
        if (sent != lastSent)
        {
            long end = now + (sent * 1000L);
            if (lastSent < 0 || Math.Abs(end - endMs) > ToleranceMs)
            {
                endMs = end;
            }

            lastSent = sent;
        }

        string time = Format(Math.Max(0, (endMs - now + 999) / 1000));
        string custom = attrs!.GetString(ClientState.TimerText, "");
        string text = custom.Length > 0 ? custom.Replace("{time}", time) : Lang.Get(KeyFor(attrs.GetString(ClientState.Kind)), time);

        if (SingleComposer == null)
        {
            Compose();
        }

        if (!IsOpened())
        {
            TryOpen(false);
        }

        if (text != shown)
        {
            shown = text;
            SingleComposer!.GetDynamicText("text").SetNewText(text);
        }

        StayBelowOthers();
    }

    private void Compose()
    {
        ElementBounds text = ElementBounds.Fixed(0, 0, Width, Height);
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterTop).WithFixedAlignmentOffset(0, GuiStyle.DialogToScreenPadding);
        CairoFont font = CairoFont.WhiteSmallishText().WithOrientation(EnumTextOrientation.Center).WithStroke([0, 0, 0, 0.8], 2);
        shown = "";
        SingleComposer = capi.Gui.CreateCompo("temporalrebirth-timer", dialog).AddDynamicText("", font, text, "text").Compose();
    }

    /// <summary>Names of blocks and boss health bars are in the same place.</summary>
    private void StayBelowOthers()
    {
        double bottom = 0;
        foreach (GuiDialog other in capi.Gui.OpenedGuis)
        {
            if (other == this || !other.ShouldReceiveRenderEvents())
            {
                continue;
            }

            foreach (GuiComposer composer in other.Composers.Values)
            {
                if (composer.Bounds.Alignment == EnumDialogArea.CenterTop)
                {
                    bottom = Math.Max(bottom, composer.Bounds.absY + composer.Bounds.OuterHeight);
                }
            }
        }

        SingleComposer.Bounds.absOffsetY = bottom + GuiElement.scaled(GuiStyle.DialogToScreenPadding);
    }

    /// <summary>"12:45", "3:12:45", "2d 03:12:45".</summary>
    internal static string Format(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.Days > 0 ? Lang.Get("temporalrebirth:timer-days", t.Days, t.ToString(@"hh\:mm\:ss"))
            : t.Hours > 0 ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");
    }

    public override void Dispose()
    {
        capi.Event.UnregisterGameTickListener(tickId);
        base.Dispose();
    }

    /// <summary>The timer says what is waited for: the same countdown means something else to a ghost and to an exile.</summary>
    private static string KeyFor(string? kind) => kind switch
    {
        nameof(Form.Ghost) => "temporalrebirth:timer-ghost",
        nameof(Form.Spectator) => "temporalrebirth:timer-spectator",
        nameof(Form.Zombie) => "temporalrebirth:timer-zombie",
        nameof(Form.Player) => "temporalrebirth:timer-player",
        _ => "temporalrebirth:msg-timer",
    };
}
