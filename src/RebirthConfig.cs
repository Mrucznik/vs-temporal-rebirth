using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TemporalRebirth;

public enum ReturnMode
{
    /// <summary>Players out of lives stay spectators until revived by a ritual or an admin.</summary>
    Disabled,

    /// <summary>Return after a fixed number of real-time hours.</summary>
    Hours,

    /// <summary>Return once the server's local clock passes <see cref="RebirthConfig.NextDayResetHour"/> on a later day.</summary>
    NextDay,
}

/// <summary>Stored in ModConfig/temporalrebirth.json on the server.</summary>
public class RebirthConfig
{
    /// <summary>Lives granted by one temporal gear. -1 follows the world's temporalGearRespawnUses setting.</summary>
    public int LivesPerGear { get; set; } = 1;

    /// <summary>Disabled, Hours or NextDay.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public ReturnMode ReturnMode { get; set; } = ReturnMode.NextDay;

    /// <summary>Used when <see cref="ReturnMode"/> is Hours.</summary>
    public double ReturnAfterHours { get; set; } = 24;

    /// <summary>Used when <see cref="ReturnMode"/> is NextDay. Hour of the server's local time (0-23).</summary>
    public int NextDayResetHour { get; set; } = 6;

    /// <summary>
    /// Ritual: hold a temporal gear (the vanilla 3.5 s use) within this many blocks of an active rift.
    /// </summary>
    public double RitualRiftRange { get; set; } = 8;

    /// <summary>Whether a ritual may revive a player who is currently offline. They come back on their next login.</summary>
    public bool RitualRevivesOffline { get; set; } = true;
}
