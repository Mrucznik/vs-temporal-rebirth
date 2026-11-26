using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace TemporalRebirth;

/// <summary>
/// Client side: a player out of lives who may not use items does not even swing what they hold, and one who may not hit
/// does not swing at creatures. The server already takes away the effect of the blow; without this the swing looked like
/// a hit that did nothing.
/// </summary>
internal class BoundHands(ICoreClientAPI api)
{
    private const long MessageIntervalMs = 3000;
    private long lastMessageMs = -MessageIntervalMs;

    public void Start() => api.Input.InWorldAction += OnAction;

    private void OnAction(EnumEntityAction action, bool on, ref EnumHandling handled)
    {
        if (action != EnumEntityAction.LeftMouseDown || !on || api.World.Player is not { Entity: { } self } player)
        {
            return;
        }

        bool noItems = self.WatchedAttributes.GetString(ClientState.NoItems) != null;
        bool noHit = self.WatchedAttributes.GetString(ClientState.NoHit) != null;
        string who = self.WatchedAttributes.GetString(ClientState.Kind) == nameof(Form.Zombie) ? "zombie" : "exile";
        string? message =
            noItems && player.InventoryManager.ActiveHotbarSlot?.Empty == false ? (noHit ? who + "-noitems" : who + "-barehands")
            : noHit && player.CurrentEntitySelection != null ? who + "-nohit"
            : null;
        if (message == null)
        {
            return;
        }

        handled = EnumHandling.PreventDefault;
        long now = api.World.ElapsedMilliseconds;
        if (now - lastMessageMs >= MessageIntervalMs)
        {
            lastMessageMs = now;
            api.TriggerIngameError(this, "temporalrebirth-boundhands", Lang.Get("temporalrebirth:" + message));
        }
    }
}
