using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>
/// What the server tells clients about a player out of lives, as watched attributes of that player: everyone near sees
/// them, they are saved with the player, and all of them go when the player comes back.
/// </summary>
internal static class ClientState
{
    /// <summary>What the player has become, a <see cref="Form"/>.</summary>
    internal const string Form = "temporalrebirthForm";

    /// <summary>How other players see a ghost, a <see cref="GhostVisible"/>. Missing when they do not.</summary>
    internal const string GhostLook = "temporalrebirthGhostLook";

    internal const string TimerSeconds = "temporalrebirthSecondsLeft";

    /// <summary>The admin's own text; without it players see the mod's in their language.</summary>
    internal const string TimerText = "temporalrebirthTimerText";

    /// <summary>What limits movement, see <see cref="LeashShape"/>. Missing when nothing does.</summary>
    internal const string Leash = "temporalrebirthLeash";

    /// <summary>The group whose land claims are the zone, and what a walking player may do there.</summary>
    internal const string ZoneGroup = "temporalrebirthZoneGroup";
    internal const string ZoneAccess = "temporalrebirthZoneAccess";

    /// <summary>Where rituals work on this server (a <see cref="RitualPlace"/>), for the gear's description. Every player has it.</summary>
    internal const string RitualWhere = "temporalrebirthRitualWhere";

    /// <summary>What the player really is, a <see cref="Form"/> (<see cref="Form"/> above is how they look), for the timer.</summary>
    internal const string Kind = "temporalrebirthKind";

    /// <summary>Set ("1") for a zombie that may not use items: its own client keeps it from swinging them.</summary>
    internal const string NoItems = "temporalrebirthNoItems";

    private static readonly string[] All = [Form, Kind, NoItems, GhostLook, TimerSeconds, TimerText, Leash, ZoneGroup, ZoneAccess];

    /// <summary>Null removes it. Unchanged values are not sent again.</summary>
    internal static void Set(EntityPlayer entity, string key, string? value)
    {
        if (value == null)
        {
            Remove(entity, key);
        }
        else if (entity.WatchedAttributes.GetString(key) != value)
        {
            entity.WatchedAttributes.SetString(key, value);
        }
    }

    internal static void Set(EntityPlayer entity, string key, int? value)
    {
        if (value == null)
        {
            Remove(entity, key);
        }
        else if (entity.WatchedAttributes.TryGetInt(key) != value)
        {
            entity.WatchedAttributes.SetInt(key, value.Value);
        }
    }

    internal static void Clear(EntityPlayer entity)
    {
        foreach (string key in All)
        {
            Remove(entity, key);
        }
    }

    private static void Remove(EntityPlayer entity, string key)
    {
        if (entity.WatchedAttributes.HasAttribute(key))
        {
            entity.WatchedAttributes.RemoveAttribute(key);
            entity.WatchedAttributes.MarkPathDirty(key);
        }
    }
}
