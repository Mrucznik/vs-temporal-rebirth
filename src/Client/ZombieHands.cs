using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace TemporalRebirth;

/// <summary>
/// Client side: a zombie that may not use items does not even swing what it holds. The server already takes away the
/// effect of the blow; without this the swing looked like a hit that did nothing.
/// </summary>
internal class ZombieHands(ICoreClientAPI api)
{
    private const long MessageIntervalMs = 3000;
    private long lastMessageMs = -MessageIntervalMs;

    public void Start() => api.Input.InWorldAction += OnAction;

    private void OnAction(EnumEntityAction action, bool on, ref EnumHandling handled)
    {
        if (action != EnumEntityAction.LeftMouseDown || !on || api.World.Player is not { Entity: { } self } player
            || self.WatchedAttributes.GetString(ClientState.NoItems) == null || player.InventoryManager.ActiveHotbarSlot?.Empty != false)
        {
            return;
        }

        handled = EnumHandling.PreventDefault;
        long now = api.World.ElapsedMilliseconds;
        if (now - lastMessageMs >= MessageIntervalMs)
        {
            lastMessageMs = now;
            api.TriggerIngameError(this, "temporalrebirth-zombiehands", Lang.Get("temporalrebirth:zombie-barehands"));
        }
    }
}
