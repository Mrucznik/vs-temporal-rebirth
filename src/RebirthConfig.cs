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

public enum RitualReviveAt
{
    /// <summary>Next to the player performing the ritual. Can be abused as a teleport to a friend.</summary>
    Caster,

    /// <summary>Where the ghost died. The ritual only shortens the wait.</summary>
    DeathSpot,

    /// <summary>At the ghost's spawn point, like the natural return.</summary>
    Spawn,
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

    /// <summary>Whether ghosts pass through blocks (vanilla spectator) or collide with them.</summary>
    public bool GhostNoClip { get; set; } = false;

    /// <summary>How far (in blocks) a ghost may drift from where it died. 0 lets ghosts roam freely.</summary>
    public double GhostTetherRadius { get; set; } = 24;

    /// <summary>
    /// Ritual: hold a temporal gear (the vanilla 3.5 s use) within this many blocks of an active rift.
    /// </summary>
    public double RitualRiftRange { get; set; } = 8;

    /// <summary>Caster, DeathSpot or Spawn: where a ghost revived by a ritual appears.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public RitualReviveAt RitualReviveAt { get; set; } = RitualReviveAt.Caster;

    /// <summary>Hours before the same player can be revived by a ritual again. 0 = no limit.</summary>
    public double RitualCooldownHours { get; set; } = 0;

    /// <summary>
    /// Caps health after coming back (ritual or natural return), in percent of max. Only lowers.
    /// 100 = vanilla (full health).
    /// </summary>
    public float ReturnHealthPercent { get; set; } = 100;

    /// <summary>
    /// Caps satiety after coming back, in percent of max. Only lowers. 100 = vanilla, which already
    /// leaves satiety at 50% and halves nutrition levels on revive.
    /// </summary>
    public float ReturnSatietyPercent { get; set; } = 100;
}
