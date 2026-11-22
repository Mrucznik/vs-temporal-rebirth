using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>Client side: without this the client refuses to touch blocks in the zone's claims before asking the server.</summary>
internal class ZonePrediction(ICoreClientAPI api)
{
    public void Start() => api.Event.OnTestBlockAccessClaim += TestAccess;

    private EnumWorldAccessResponse TestAccess(IPlayer player, BlockSelection selection, EnumBlockAccessFlags wanted, ref string claimant, LandClaim claim, EnumWorldAccessResponse response)
    {
        // The game passes a null selection when nothing is targeted.
        if (selection?.Position == null || player.Entity?.WatchedAttributes is not { } attrs || !Enum.TryParse(attrs.GetString(ClientState.ZoneAccess), out ZoneAccess access))
        {
            return response;
        }

        return Zones.Decide(api.World.Claims, selection.Position, attrs.GetInt(ClientState.ZoneGroup), access, wanted, claim, response, ref claimant);
    }
}
