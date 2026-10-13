using System.Globalization;

namespace GhNotify.Core;

public static class WorkHours
{
    public static bool IsWithin(WorkHoursConfig cfg, DateTime local)
    {
        var start = Parse(cfg.Start);
        var end = Parse(cfg.End);
        var t = TimeOnly.FromDateTime(local);
        if (start == end) return IsDay(cfg, local.DayOfWeek);
        if (start < end) return IsDay(cfg, local.DayOfWeek) && t >= start && t < end;
        // Overnight: opens on a listed day at start, closes the next morning at end.
        return (t >= start && IsDay(cfg, local.DayOfWeek)) || (t < end && IsDay(cfg, local.AddDays(-1).DayOfWeek));
    }

    public static void Validate(WorkHoursConfig cfg)
    {
        Parse(cfg.Start);
        Parse(cfg.End);
        foreach (var d in cfg.Days)
            if (ToDay(d) is null) throw new InvalidDataException($"workHours.days: unknown day '{d}'");
    }

    static TimeOnly Parse(string s) =>
        TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : throw new InvalidDataException($"workHours: '{s}' is not HH:mm");

    static bool IsDay(WorkHoursConfig cfg, DayOfWeek day) => cfg.Days.Any(d => ToDay(d) == day);

    static DayOfWeek? ToDay(string s)
    {
        if (s.Length < 3) return null;
        foreach (var d in Enum.GetValues<DayOfWeek>())
            if (d.ToString().StartsWith(s, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }
}
