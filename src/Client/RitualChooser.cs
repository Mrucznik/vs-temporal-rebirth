using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TemporalRebirth;

/// <summary>
/// Client side: whom the ritual should bring back, when more than one can be. A click sends the choice; the server checks
/// it again and only then takes the gear. Closing it in any other way keeps the gear.
/// </summary>
internal class RitualChooser : GuiDialog
{
    private const double RowHeight = 34, NameWidth = 190, DetailWidth = 300, Gap = 10;

    private readonly ChooserOpenPacket offer;
    private readonly Action<ChooserPickPacket> send;
    private readonly Vec3d from;
    private readonly long untilMs;
    private readonly long tickId;
    private bool answered;

    public RitualChooser(ICoreClientAPI api, ChooserOpenPacket offer, Action<ChooserPickPacket> send)
        : base(api)
    {
        this.offer = offer;
        this.send = send;
        from = new Vec3d(offer.X, offer.Y, offer.Z);
        // A little sooner than the server, so a click at the last moment is not refused.
        untilMs = api.World.ElapsedMilliseconds + ((RitualNet.ChooserSeconds - 1) * 1000L);
        tickId = api.Event.RegisterGameTickListener(_ => Watch(), 250);
        Compose();
    }

    public override string? ToggleKeyCombinationCode => null;

    private void Compose()
    {
        double width = NameWidth + Gap + DetailWidth;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        ElementBounds background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        CairoFont detail = CairoFont.WhiteSmallText();

        GuiComposer composer = capi.Gui.CreateCompo("temporalrebirth-chooser", dialog)
            .AddShadedDialogBG(background)
            .AddDialogTitleBar(Lang.Get("temporalrebirth:chooser-title"), () => TryClose());
        composer.BeginChildElements(background);

        double y = GuiStyle.TitleBarHeight + 6;
        foreach (ChooserEntry entry in offer.Entries)
        {
            string uid = entry.Uid;
            composer.AddButton(entry.Name, () => Answer(new ChooserPickPacket { Uid = uid }), ElementBounds.Fixed(0, y, NameWidth, RowHeight - 6));
            composer.AddStaticText(Details(entry), detail, ElementBounds.Fixed(NameWidth + Gap, y + 6, DetailWidth, RowHeight - 6));
            y += RowHeight;
        }

        y += 8;
        if (offer.AllowBind)
        {
            composer.AddSmallButton(Lang.Get("temporalrebirth:chooser-bind"), () => Answer(new ChooserPickPacket { Bind = true }), ElementBounds.Fixed(0, y, width - 120 - Gap, 28));
        }

        composer.AddSmallButton(Lang.Get("Cancel"), () => TryClose(), ElementBounds.Fixed(width - 120, y, 120, 28));
        composer.EndChildElements();
        SingleComposer = composer.Compose();
    }

    /// <summary>"ghost · 3:12:45 left · online"</summary>
    private static string Details(ChooserEntry entry)
    {
        string form = Lang.Get("temporalrebirth:form-" + ((Form)entry.Form).ToString().ToLowerInvariant());
        string time = entry.SecondsLeft < 0 ? Lang.Get("temporalrebirth:chooser-until-revived") : Lang.Get("temporalrebirth:chooser-left", TimerHud.Format(entry.SecondsLeft));
        string online = Lang.Get(entry.Online ? "temporalrebirth:chooser-online" : "temporalrebirth:chooser-offline");
        return form + " · " + time + " · " + online;
    }

    private bool Answer(ChooserPickPacket pick)
    {
        answered = true;
        send(pick);
        TryClose();
        return true;
    }

    /// <summary>The server closes it too; this only spares a click that would be refused.</summary>
    private void Watch()
    {
        var entity = capi.World.Player?.Entity;
        if (entity == null || !entity.Alive || capi.World.ElapsedMilliseconds > untilMs || entity.Pos.XYZ.DistanceTo(from) > RitualNet.ChooserMoveLimit)
        {
            TryClose();
        }
    }

    /// <summary>Closed by the server, which already dropped the choice.</summary>
    public void Dismiss()
    {
        answered = true;
        capi.Event.UnregisterGameTickListener(tickId);
        TryClose();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        capi.Event.UnregisterGameTickListener(tickId);
        if (!answered)
        {
            answered = true;
            send(new ChooserPickPacket { Cancel = true });
        }
    }
}
