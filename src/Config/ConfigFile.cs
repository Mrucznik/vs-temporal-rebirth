using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TemporalRebirth;

/// <summary>
/// ModConfig/temporalrebirth.json. Rules: an admin's file is never overwritten when something in it could not be read,
/// and every rewrite of an existing file leaves a .bak copy.
/// </summary>
internal sealed class ConfigFile(string path, Action<string> warn)
{
    private static readonly string[] OldKeys =
    [
        "LivesPerGear", "ReturnMode", "ReturnAfterHours", "NextDayResetHour", "GhostNoClip", "GhostTetherRadius",
        "RitualRiftRange", "RitualReviveAt", "RitualCooldownHours", "ReturnHealthPercent", "ReturnSatietyPercent",
    ];

    /// <summary>Where the previous file is kept.</summary>
    public string BackupName => Path.GetFileName(path) + ".bak";

    public RebirthConfig Load()
    {
        var values = ConfigSchema.Defaults();
        if (!File.Exists(path))
        {
            Write(values);
            return new RebirthConfig(values);
        }

        JObject json;
        try
        {
            json = JObject.Parse(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            warn($"{Path.GetFileName(path)} is invalid, using defaults until it is fixed: {e.Message}");
            return new RebirthConfig(values);
        }

        bool old = OldKeys.Any(k => json.ContainsKey(k)) && !json.ContainsKey("OutOfLives");
        Dictionary<string, JToken> given = old ? MigrateOld(json) : Flatten(json);
        bool rewrite = old, problems = false;

        if (!old)
        {
            if (given.Remove("ConfigVersion", out JToken? version) && version.Type == JTokenType.Integer && (int)version > ConfigSchema.Version)
            {
                warn($"{Path.GetFileName(path)} was made for a newer version of the mod");
                problems = true;
            }

            given.Remove("ApplyPreset", out JToken? wanted);
            if (wanted?.Type == JTokenType.String && (string)wanted! is { Length: > 0 } id)
            {
                if (ConfigSchema.FindPreset(id) is { } preset)
                {
                    foreach (string key in given.Keys.Where(k => ConfigSchema.ByKey.TryGetValue(k, out ConfigField? f) && !f.Kept).ToList())
                    {
                        given.Remove(key);
                    }

                    values = ConfigSchema.Apply(preset, values);
                    rewrite = true;
                }
                else
                {
                    warn($"ApplyPreset: unknown preset {id}. Accepted: {string.Join(", ", ConfigSchema.Presets.Select(p => p.Id))}");
                    problems = true;
                }
            }
        }

        problems |= !Merge(given, values);
        if (!problems && (rewrite || given.Count < ConfigSchema.Fields.Length))
        {
            Write(values);
        }

        return new RebirthConfig(values);
    }

    public void Write(IReadOnlyDictionary<string, JToken> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
        }

        File.WriteAllText(path, Format(values));
    }

    /// <summary>Copies valid settings over; warns about the rest and returns false.</summary>
    private bool Merge(Dictionary<string, JToken> given, Dictionary<string, JToken> values)
    {
        bool clean = true;
        foreach (var (key, value) in given)
        {
            if (!ConfigSchema.ByKey.TryGetValue(key, out ConfigField? field))
            {
                warn($"{key}: unknown setting, skipped");
                clean = false;
            }
            else if (field.Validate(value, out string allowed) is { } valid)
            {
                values[field.Key] = valid;
            }
            else
            {
                warn($"{key}: {value.ToString(Formatting.None)} is not accepted, using {values[field.Key].ToString(Formatting.None)}. Accepted: {allowed}");
                clean = false;
            }
        }

        return clean;
    }

    private static Dictionary<string, JToken> Flatten(JObject json, string prefix = "")
    {
        var flat = new Dictionary<string, JToken>();
        foreach (JProperty property in json.Properties())
        {
            if (property.Value is JObject inner)
            {
                foreach (var (key, value) in Flatten(inner, prefix + property.Name + "."))
                {
                    flat[key] = value;
                }
            }
            else
            {
                flat[prefix + property.Name] = property.Value;
            }
        }

        return flat;
    }

    /// <summary>Flat config of 0.6.x. Must match migrateOld in docs/configurator/logic.js.</summary>
    private static Dictionary<string, JToken> MigrateOld(JObject old)
    {
        var made = new Dictionary<string, JToken>();
        void Copy(string from, string to)
        {
            if (old.TryGetValue(from, out JToken? value))
            {
                made[to] = value;
            }
        }

        double? Number(string key) => old[key]?.Type is JTokenType.Integer or JTokenType.Float ? (double)old[key]! : null;

        // 0.6.x took any number, e.g. a negative distance for no limit.
        void CopyWithin(string from, string to)
        {
            Copy(from, to);
            if (Number(from) is { } n)
            {
                made[to] = Math.Clamp(n, ConfigSchema.ByKey[to].Min, ConfigSchema.ByKey[to].Max);
            }
        }

        Copy("LivesPerGear", "Lives.PerGear");
        if (old["GhostNoClip"]?.Type == JTokenType.Boolean && (bool)old["GhostNoClip"]!)
        {
            // A ghost that passes through walls is what the new config calls a spectator.
            made["OutOfLives"] = "Spectator";
        }

        CopyWithin("GhostTetherRadius", "Place.Radius");
        // Neither existed in 0.6.x; an update must not change what players see.
        made["Ghost.Visible"] = "Nobody";
        made["Time.ShowTimer"] = false;
        if (Number("RitualRiftRange") <= 0)
        {
            // The only way to turn rituals off in 0.6.x.
            made["Ritual.Where"] = "Disabled";
        }
        else
        {
            CopyWithin("RitualRiftRange", "Ritual.Range");
        }

        CopyWithin("RitualCooldownHours", "Ritual.CooldownHours");
        // Old percentages only lowered what the game gives (full health, 50% satiety); new ones set it exactly.
        if (Number("ReturnHealthPercent") is { } health and < 100)
        {
            made["Return.HealthPercent"] = Math.Max(1, health);
        }

        if (Number("ReturnSatietyPercent") is { } satiety and < 50)
        {
            made["Return.SatietyPercent"] = Math.Max(0, satiety);
        }

        if (old["RitualReviveAt"] is { } reviveAt)
        {
            made["Ritual.ReviveAt"] = (string?)reviveAt == "DeathSpot" ? "Body" : reviveAt;
        }

        int hour = (int)Math.Clamp(Number("NextDayResetHour") ?? 6, 0, 23);
        made["Time.Duration"] = (string?)old["ReturnMode"] switch
        {
            "Disabled" => "forever",
            "Hours" => Math.Clamp(Number("ReturnAfterHours") ?? 24, 0.01, 10000).ToString(CultureInfo.InvariantCulture) + "h",
            _ => $"next day {hour:00}:00",
        };

        // 0.6.x stored decimals ("24.0"); the new fields are whole numbers.
        foreach (string key in made.Keys.ToList())
        {
            if (ConfigSchema.ByKey[key].Type == "int" && made[key].Type == JTokenType.Float)
            {
                made[key] = (long)Math.Round((double)made[key], MidpointRounding.AwayFromZero);
            }
        }

        return made;
    }

    /// <summary>The file with a comment on every setting. Must match toFile in docs/configurator/logic.js.</summary>
    internal static string Format(IReadOnlyDictionary<string, JToken> values)
    {
        var text = new StringBuilder("{\n");
        void Comment(string indent, ConfigField f)
        {
            var parts = new List<string?> { f.Label + ".", f.Special is { } s ? s.Value + " = " + s.Label.ToLowerInvariant() + "." : null, f.Help, f.FileNote };
            if (f.Type == "enum")
            {
                parts.Add(string.Join(", ", f.Options.Select(o => o.Value + " = " + o.Label.ToLowerInvariant())) + ".");
            }

            if (f.IsMessage)
            {
                parts.Add((f.Vars!.Length > 0 ? "Można użyć: " + string.Join(", ", f.Vars.Select(v => "{" + v + "}")) + ". " : "") + "Puste = tekst moda.");
            }

            if (f.Privilege != null)
            {
                parts.Add("Uprawnienie: " + f.Privilege + ".");
            }

            text.Append(indent).Append("// ").Append(string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)))).Append('\n');
        }

        text.Append("  \"ConfigVersion\": ").Append(ConfigSchema.Version).Append(",\n");
        text.Append("  // Gotowy preset: ").Append(string.Join(", ", ConfigSchema.Presets.Select(p => p.Id))).Append(".\n");
        text.Append("  // Wpisz nazwę i zrestartuj serwer. Mod wypełni resztę i wyczyści to pole.\n");
        text.Append("  \"ApplyPreset\": \"\",\n");

        string[] blocks = ConfigSchema.Fields.Select(f => f.Key.Split('.')[0]).Distinct().ToArray();
        for (int b = 0; b < blocks.Length; b++)
        {
            string end = b < blocks.Length - 1 ? ",\n" : "\n";
            text.Append('\n');
            if (ConfigSchema.ByKey.TryGetValue(blocks[b], out ConfigField? single))
            {
                Comment("  ", single);
                text.Append("  ").Append(JsonConvert.ToString(single.Key)).Append(": ").Append(values[single.Key].ToString(Formatting.None)).Append(end);
                continue;
            }

            text.Append("  ").Append(JsonConvert.ToString(blocks[b])).Append(": {\n");
            ConfigField[] members = ConfigSchema.Fields.Where(f => f.Key.StartsWith(blocks[b] + ".", StringComparison.Ordinal)).ToArray();
            for (int i = 0; i < members.Length; i++)
            {
                Comment("    ", members[i]);
                text.Append("    ").Append(JsonConvert.ToString(members[i].Key[(blocks[b].Length + 1)..])).Append(": ")
                    .Append(values[members[i].Key].ToString(Formatting.None)).Append(i < members.Length - 1 ? ",\n" : "\n");
            }

            text.Append("  }").Append(end);
        }

        return text.Append("}\n").ToString();
    }

    /// <summary>
    /// Reads a code made by the configurator (/rebirth import). Administration settings in it are ignored.
    /// Returns null when the code cannot be read.
    /// </summary>
    public static Dictionary<string, JToken>? ReadCode(string code, IReadOnlyDictionary<string, JToken> current, List<string> problems)
    {
        JObject json;
        try
        {
            string base64 = code.Trim().Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            json = JObject.Parse(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64)));
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return null;
        }

        if (ConfigSchema.FindPreset((string?)json["p"] ?? "") is not { } preset)
        {
            return null;
        }

        // A code describes the complete settings, so lives and messages not in it are at their defaults.
        var values = ConfigSchema.Apply(preset, ConfigSchema.Defaults());
        foreach (ConfigField field in ConfigSchema.Fields.Where(f => f.FileOnly))
        {
            values[field.Key] = current[field.Key];
        }

        foreach (JProperty property in (json["s"] as JObject ?? []).Properties())
        {
            if (!ConfigSchema.ByKey.TryGetValue(property.Name, out ConfigField? field))
            {
                problems.Add(property.Name + ": unknown setting, skipped");
            }
            else if (!field.FileOnly)
            {
                if (field.Validate(property.Value, out string allowed) is { } valid)
                {
                    values[field.Key] = valid;
                }
                else
                {
                    problems.Add($"{field.Key}: not accepted, kept the default. Accepted: {allowed}");
                }
            }
        }

        return values;
    }
}
