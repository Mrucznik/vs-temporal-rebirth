using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace TemporalRebirth;

/// <summary>
/// Soft compatibility with the Downed mod (modid "downed"), without referencing its assembly.
///
/// Downed keeps players alive in a downed state and only calls Entity.Die() once they bleed
/// out, give up or are executed, so the vanilla death counter stays correct and we need no
/// special handling for the downed state itself. What Downed does expect is the vanilla
/// respawn event, where it clears its "died while downed" flags. We revive players ourselves
/// (spectator conversion, rituals), so we clear those flags the same way.
/// </summary>
internal static class DownedCompat
{
    private static readonly string[] DownedFlags = ["downeddied", "downedexecuted"];

    internal static void ClearDeathFlags(EntityPlayer entity)
    {
        foreach (string flag in DownedFlags)
        {
            if (entity.WatchedAttributes.HasAttribute(flag))
            {
                entity.WatchedAttributes.RemoveAttribute(flag);
                entity.WatchedAttributes.MarkPathDirty(flag);
            }
        }
    }

    /// <summary>A downed player is alive but helpless; they cannot take part in a ritual.</summary>
    internal static bool IsDowned(Entity entity) => entity.WatchedAttributes.GetBool("downed", false);
}
