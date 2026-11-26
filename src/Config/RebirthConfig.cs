using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TemporalRebirth;

internal enum Form { Ghost, Spectator, Zombie, Player, Ban }

internal enum GhostVisible { Nobody, Wisp, WispAndEcho }

internal enum Place { Body, Spawn, Fixed, Zone }

internal enum RitualPlace { Disabled, Rift, NearPlayer, FixedPlace, Anywhere }

internal enum ReviveAt { Caster, InPlace, Body, Spawn }

/// <summary>Typed view of the settings. The meaning of every key is in docs/configurator/schema.js.</summary>
internal class RebirthConfig(IReadOnlyDictionary<string, JToken> values)
{
    public IReadOnlyDictionary<string, JToken> Values { get; private set; } = values;

    public static RebirthConfig Default() => new(ConfigSchema.Defaults());

    /// <summary>Changes the settings in place, so everyone holding this object sees them.</summary>
    public void Replace(IReadOnlyDictionary<string, JToken> values) => Values = values;

    public int LivesPerGear => Int("Lives.PerGear");
    public int LivesMax => Int("Lives.Max");
    public LivesCount LivesCount => Choice<LivesCount>("Lives.Count");

    public Form Form => Choice<Form>("OutOfLives");

    /// <summary>Zombies and normal players; ghosts and spectators fly.</summary>
    public bool Walks => Form is Form.Zombie or Form.Player;

    public GhostVisible GhostVisible => Choice<GhostVisible>("Ghost.Visible");
    public bool ZombieCanUseItems => Flag("Zombie.CanUseItems");
    public bool ZombieGlow => Flag("Zombie.Glow");

    public Place PlaceAt => Choice<Place>("Place.At");
    public int Radius => Int("Place.Radius");
    public string PlaceGroup => Text("Place.Group");
    public ZoneAccess PlaceAccess => Choice<ZoneAccess>("Place.Access");

    public Duration Duration => Duration.TryParse(Text("Time.Duration"), out Duration d) ? d : throw new InvalidOperationException("validated on load");
    public bool ShowTimer => Flag("Time.ShowTimer");

    public Place ReturnAt => Choice<Place>("Return.At");
    public int ReturnLives => Int("Return.Lives");
    public bool DieAtEnd => Flag("Return.DieAtEnd");
    public int HealthPercent => Int("Return.HealthPercent");
    public int SatietyPercent => Int("Return.SatietyPercent");

    public RitualPlace RitualWhere => Choice<RitualPlace>("Ritual.Where");
    public int RitualRange => Int("Ritual.Range");
    public ReviveAt RitualReviveAt => Choice<ReviveAt>("Ritual.ReviveAt");
    public double RitualCooldownHours => (double)Values["Ritual.CooldownHours"];
    public bool RitualLiftsBan => Flag("Ritual.CanLiftBan");
    public int RitualHealthCost => Int("Ritual.HealthCost");
    public int RitualHealthMin => Int("Ritual.HealthMin");
    public bool RitualSelfAtRift => Flag("Ritual.SelfAtRift");

    /// <summary>The admin's own text for a message, or null to use the mod's.</summary>
    public string? Message(string key) => Text("Messages." + key) is { Length: > 0 } text ? text : null;

    /// <summary>Whether an action may be used in game at all; see <see cref="Rights"/> for who may use it.</summary>
    public bool Allows(string action) => Flag("Admin.Allow" + action);

    /// <summary>
    /// What one player's time out of lives is bound to (sections marked "atDeath" in schema.js): later changes of the
    /// settings only affect later deaths.
    /// </summary>
    public string Snapshot() =>
        JsonConvert.SerializeObject(Values.Where(v => ConfigSchema.ByKey.TryGetValue(v.Key, out ConfigField? f) && f.AtDeath).ToDictionary(v => v.Key, v => v.Value));

    /// <summary>These settings with a snapshot on top.</summary>
    public RebirthConfig With(string? snapshot)
    {
        if (snapshot == null)
        {
            return this;
        }

        var merged = new Dictionary<string, JToken>(Values);
        foreach (var (key, value) in JsonConvert.DeserializeObject<Dictionary<string, JToken>>(snapshot)!)
        {
            if (ConfigSchema.ByKey.TryGetValue(key, out ConfigField? field) && field.Validate(value, out _) is { } valid)
            {
                merged[key] = valid;
            }
        }

        return new RebirthConfig(merged);
    }

    private string Text(string key) => (string)Values[key]!;

    private int Int(string key) => (int)Values[key];

    private bool Flag(string key) => (bool)Values[key];

    private T Choice<T>(string key)
        where T : struct, Enum => Enum.Parse<T>(Text(key));
}
