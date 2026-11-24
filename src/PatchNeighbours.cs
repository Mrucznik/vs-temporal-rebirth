using HarmonyLib;
using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>
/// Tells the admin which other mods change the same game methods as this mod. Two mods patching one method may still get
/// along, but when something misbehaves (a gear that does nothing, a death screen that is wrong), this line in the log
/// is where to start. Downed is left out: it was tested together with this mod.
/// </summary>
internal static class PatchNeighbours
{
    private static readonly string[] Tested = ["downed"];

    public static void Warn(ICoreAPI api, Harmony ours)
    {
        foreach (var method in ours.GetPatchedMethods())
        {
            HarmonyLib.Patches? info = Harmony.GetPatchInfo(method);
            if (info == null)
            {
                continue;
            }

            var others = info.Owners
                .Where(owner => !owner.StartsWith("temporalrebirth", StringComparison.OrdinalIgnoreCase))
                .Where(owner => !Tested.Any(t => owner.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (others.Count > 0)
            {
                api.Logger.ModWarning(
                    "{0}.{1} is also changed by: {2}. If it misbehaves, try without that mod and report it.",
                    method.DeclaringType?.Name, method.Name, string.Join(", ", others));
            }
        }
    }
}
