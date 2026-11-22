using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace TemporalRebirth;

/// <summary>One setting, as described in docs/configurator/schema.js.</summary>
internal sealed class ConfigField
{
    public ConfigField(JObject json, JObject section)
    {
        Key = (string)json["key"]!;
        Type = (string)json["type"]!;
        Default = json["default"]!;
        Min = (double?)json["min"] ?? double.MinValue;
        Max = (double?)json["max"] ?? double.MaxValue;
        Not = json["not"]?.Values<double>().ToArray() ?? [];
        MaxLength = (int?)json["maxLength"] ?? int.MaxValue;
        Pattern = (string?)json["pattern"];
        Vars = json["vars"]?.Values<string>().Select(v => v!).ToArray();
        Options = json["options"]?.Select(o => ((string)o["value"]!, (string)o["label"]!)).ToArray() ?? [];
        Special = json["special"] is JObject s ? ((string)s["value"]!, (string)s["label"]!) : null;
        Privilege = (string?)json["privilege"];
        IsMessage = json["builtin"] != null;
        Kept = (bool?)section["keep"] ?? false;
        FileOnly = (bool?)section["fileOnly"] ?? false;
    }

    public string Key { get; }
    public string Type { get; }
    public JToken Default { get; }
    public double Min { get; }
    public double Max { get; }
    public double[] Not { get; }
    public int MaxLength { get; }
    public string? Pattern { get; }

    /// <summary>Placeholders a message may use; null when the field is not a message.</summary>
    public string[]? Vars { get; }

    public (string Value, string Label)[] Options { get; }
    public (string Value, string Label)? Special { get; }
    public string? Privilege { get; }
    public bool IsMessage { get; }

    /// <summary>Presets leave these alone (lives, administration, messages).</summary>
    public bool Kept { get; }

    /// <summary>Only the file may change these: an admin must not lift their own limits with a command.</summary>
    public bool FileOnly { get; }

    /// <summary>Returns the normalized value, or null and a description of what is accepted.</summary>
    public JToken? Validate(JToken? value, out string allowed)
    {
        allowed = "";
        switch (Type)
        {
            case "bool":
                allowed = "true, false";
                return value?.Type == JTokenType.Boolean ? value : null;

            case "enum":
                allowed = string.Join(", ", Options.Select(o => o.Value));
                return value?.Type == JTokenType.String
                    ? Options.Where(o => o.Value.Equals((string)value!, StringComparison.OrdinalIgnoreCase)).Select(o => (JToken)o.Value).FirstOrDefault()
                    : null;

            case "duration":
                allowed = "\"12h\", \"7d\", \"next day 06:00\", \"forever\"";
                return value?.Type == JTokenType.String && Duration.TryParse((string)value!, out Duration d) ? (JToken)d.ToString() : null;

            case "text":
                return ValidateText(value, out allowed);

            default:
                allowed = Number(Min) + " … " + Number(Max) + (Not.Length == 0 ? "" : " (≠ " + string.Join(", ≠ ", Not.Select(Number)) + ")");
                if (value?.Type is not (JTokenType.Integer or JTokenType.Float))
                {
                    return null;
                }

                double n = (double)value;
                if (!double.IsFinite(n) || n < Min || n > Max || Not.Contains(n) || (Type == "int" && n != Math.Floor(n)))
                {
                    return null;
                }

                return Type == "int" ? (JToken)(long)n : n == Math.Floor(n) ? (JToken)(long)n : (JToken)n;
        }
    }

    private JToken? ValidateText(JToken? value, out string allowed)
    {
        allowed = "text, max " + MaxLength;
        if (value?.Type != JTokenType.String)
        {
            return null;
        }

        string text = (string)value!;
        if (Vars != null && Regex.Matches(text, @"\{([^{}]*)\}").Any(m => !Vars.Contains(m.Groups[1].Value)))
        {
            // A mistyped {placeholder} would reach players as is.
            allowed = string.Join(", ", Vars.Select(v => "{" + v + "}"));
            return null;
        }

        return text.Length <= MaxLength && (Pattern == null || Regex.IsMatch(text, Pattern)) ? value : null;
    }

    /// <summary>Parses what an admin typed in a command.</summary>
    public JToken? ParseAndValidate(string text, out string allowed)
    {
        JToken? value = Type switch
        {
            "bool" => text.ToLowerInvariant() switch { "true" or "on" or "yes" or "1" => true, "false" or "off" or "no" or "0" => false, _ => null },
            "int" or "number" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null,
            _ => text,
        };
        return Validate(value, out allowed);
    }

    private static string Number(double n) => n.ToString(CultureInfo.InvariantCulture);
}

internal sealed record ConfigPreset(string Id, IReadOnlyDictionary<string, JToken> Set);

/// <summary>
/// The schema shared with the web configurator: keys, ranges, presets and comment texts. Read from the embedded
/// docs/configurator/schema.js, so the page and the mod cannot disagree.
/// </summary>
internal static class ConfigSchema
{
    static ConfigSchema()
    {
        using Stream stream = typeof(ConfigSchema).Assembly.GetManifestResourceStream("schema.js")!;
        string text = new StreamReader(stream).ReadToEnd();
        const string Start = "window.TR_SCHEMA =";
        var json = JObject.Parse(text[(text.IndexOf(Start, StringComparison.Ordinal) + Start.Length)..].Trim().TrimEnd(';'));

        Version = (int)json["version"]!;
        var sections = json["sections"]!.Cast<JObject>().ToDictionary(s => (string)s["id"]!);
        Fields = json["fields"]!.Cast<JObject>().Select(f => new ConfigField(f, sections[(string)f["section"]!])).ToArray();
        ByKey = Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        Presets = json["presets"]!
            .Select(p => new ConfigPreset((string)p["id"]!, ((JObject)p["set"]!).Properties().ToDictionary(x => x.Name, x => x.Value)))
            .ToArray();
    }

    public static int Version { get; }
    public static ConfigField[] Fields { get; }
    public static IReadOnlyDictionary<string, ConfigField> ByKey { get; }
    public static ConfigPreset[] Presets { get; }

    public static ConfigPreset? FindPreset(string id) => Presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static Dictionary<string, JToken> Defaults() => Fields.ToDictionary(f => f.Key, f => f.Default);

    /// <summary>The preset's settings; lives, administration and messages are taken from <paramref name="current"/>.</summary>
    public static Dictionary<string, JToken> Apply(ConfigPreset preset, IReadOnlyDictionary<string, JToken> current)
    {
        var values = Defaults();
        foreach (var (key, value) in preset.Set)
        {
            values[key] = value;
        }

        foreach (ConfigField field in Fields.Where(f => f.Kept))
        {
            values[field.Key] = current[field.Key];
        }

        return values;
    }
}
