using System.Globalization;
using System.Text.RegularExpressions;

namespace TemporalRebirth;

/// <summary>"12h", "7d", "next day 06:00" (the server's local clock) or "forever". Real time, not game time.</summary>
internal readonly struct Duration
{
    private readonly double? hours;
    private readonly TimeSpan? clock;
    private readonly string text;

    private Duration(string text, double? hours, TimeSpan? clock)
    {
        this.text = text;
        this.hours = hours;
        this.clock = clock;
    }

    public static bool TryParse(string input, out Duration duration)
    {
        string t = input.Trim().ToLowerInvariant();
        duration = new Duration("forever", null, null);
        if (t == "forever")
        {
            return true;
        }

        Match m = Regex.Match(t, @"^(\d+(?:\.\d+)?)\s*([hd])$");
        if (m.Success)
        {
            double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            duration = new Duration(n.ToString(CultureInfo.InvariantCulture) + m.Groups[2].Value, m.Groups[2].Value == "d" ? n * 24 : n, null);
            return n is > 0 and <= 10000;
        }

        m = Regex.Match(t, @"^next day (\d{1,2}):(\d{2})$");
        if (!m.Success)
        {
            return false;
        }

        int hour = int.Parse(m.Groups[1].Value), minute = int.Parse(m.Groups[2].Value);
        duration = new Duration($"next day {hour:00}:{minute:00}", null, new TimeSpan(hour, minute, 0));
        return hour < 24 && minute < 60;
    }

    /// <summary>When it ends, or null when it never does.</summary>
    public DateTime? EndUtc(DateTime startUtc)
    {
        if (hours is { } h)
        {
            return startUtc.AddHours(h);
        }

        return clock is { } time ? (startUtc.ToLocalTime().Date.AddDays(1) + time).ToUniversalTime() : null;
    }

    public override string ToString() => text;
}
