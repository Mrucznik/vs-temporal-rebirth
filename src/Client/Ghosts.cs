using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>Client side: the ghosts that can be seen. Looked up a few times a second, drawn every frame.</summary>
internal class Ghosts(ICoreClientAPI api)
{
    private readonly List<(IPlayer Player, GhostVisible Look)> visible = new();

    /// <summary>Includes the player's own ghost. Entities may have gone since the last look.</summary>
    public List<(IPlayer Player, GhostVisible Look)> Visible => visible;

    public void Start() => api.Event.RegisterGameTickListener(_ => Refresh(), 250);

    private void Refresh()
    {
        visible.Clear();
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player.Entity != null && player.WorldData?.CurrentGameMode == EnumGameMode.Spectator
                && Enum.TryParse(player.Entity.WatchedAttributes.GetString(ClientState.GhostLook), out GhostVisible look) && look != GhostVisible.Nobody)
            {
                visible.Add((player, look));
            }
        }
    }
}
