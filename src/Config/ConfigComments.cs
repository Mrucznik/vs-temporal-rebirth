using Newtonsoft.Json;

namespace TemporalRebirth;

/// <summary>
/// Comments of the config file. Generated from the configurator's texts (docs/configurator/i18n/comments.json), so the
/// page and the mod write the same file.
/// </summary>
internal sealed record ConfigComments(string[] Preset, Dictionary<string, string> Fields)
{
    private const string Fallback = "en";

    private static readonly Dictionary<string, ConfigComments> All = Read();

    public static ConfigComments In(string? locale) =>
        All.GetValueOrDefault((locale ?? Fallback).Split('-')[0].ToLowerInvariant()) ?? All[Fallback];

    private static Dictionary<string, ConfigComments> Read()
    {
        using Stream stream = typeof(ConfigComments).Assembly.GetManifestResourceStream("comments.json")!;
        return JsonConvert.DeserializeObject<Dictionary<string, ConfigComments>>(new StreamReader(stream).ReadToEnd())!;
    }
}
