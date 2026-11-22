using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>Client side: tints zombies and gives them a faint glow.</summary>
internal class ZombieLook(ICoreClientAPI api)
{
    // Pale green; light as hue, saturation, brightness in the game's own ranges.
    private const int Tint = unchecked((int)0xFF9FCF8F);
    private static readonly byte[] Glow = [24, 4, 6];

    private readonly HashSet<long> marked = new();

    public void Start() => api.Event.RegisterGameTickListener(_ => Update(), 250);

    private void Update()
    {
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player.Entity is not { } entity)
            {
                continue;
            }

            if (entity.WatchedAttributes.GetString(ClientState.Form) == nameof(Form.Zombie))
            {
                entity.RenderColor = Tint;
                entity.LightHsv = Glow;
                marked.Add(entity.EntityId);
            }
            else if (marked.Remove(entity.EntityId))
            {
                entity.RenderColor = -1;
                entity.LightHsv = null;
            }
        }
    }
}
