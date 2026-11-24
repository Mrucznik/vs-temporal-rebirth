using Vintagestory.API.Common;

namespace TemporalRebirth;

/// <summary>The game's logger with the mod's prefix, so server admins can grep for it.</summary>
internal static class ModLog
{
    private const string Prefix = "[temporalrebirth] ";

    public static void ModError(this ILogger logger, string format, params object?[] args) => logger.Error(Prefix + format, args);

    public static void ModWarning(this ILogger logger, string format, params object?[] args) => logger.Warning(Prefix + format, args);

    public static void ModNotification(this ILogger logger, string format, params object?[] args) => logger.Notification(Prefix + format, args);

    public static void ModVerboseDebug(this ILogger logger, string format, params object?[] args) => logger.VerboseDebug(Prefix + format, args);

    public static void ModAudit(this ILogger logger, string format, params object?[] args) => logger.Audit(Prefix + format, args);
}
