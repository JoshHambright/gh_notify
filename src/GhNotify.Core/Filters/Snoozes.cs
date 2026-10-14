using System.Globalization;

namespace GhNotify.Core;

public enum SnoozeChoice { OneHour, FourHours, Tomorrow, NextWeek, UntilChange }
public enum MuteScope { Repo, Project, Org }
public enum MuteDuration { OneHour, Tomorrow, Forever }

public sealed record SnoozeResult(IReadOnlyList<Snooze> Kept, IReadOnlyList<InboxItem> Woken);

public static class Snoozes
{
    /// <summary>Wakes snoozes whose time passed or whose item changed; drops snoozes for items that left.</summary>
    public static SnoozeResult Evaluate(IReadOnlyList<Snooze> snoozes, InboxDiff diff, IReadOnlyCollection<InboxEntry> present, DateTimeOffset now)
    {
        var byKey = present.GroupBy(e => e.Item.Key).ToDictionary(g => g.Key, g => g.First().Item);
        var changed = diff.Changed.Select(c => c.New.Key).ToHashSet();
        var kept = new List<Snooze>();
        var woken = new List<InboxItem>();
        foreach (var s in snoozes)
        {
            if (!byKey.TryGetValue(s.Key, out var item)) continue;
            if (changed.Contains(s.Key) || s.Until is { } u && u <= now) woken.Add(item);
            else kept.Add(s);
        }
        return new SnoozeResult(kept, woken);
    }

    public static IReadOnlyList<TimedMute> PruneMutes(IReadOnlyList<TimedMute> mutes, DateTimeOffset now) =>
        mutes.Where(m => m.Until is not { } u || u > now).ToList();

    public static string Remaining(Snooze s, DateTimeOffset now) => s.Until is not { } u ? "until it changes" : Left(u - now, u);

    public static string Left(TimeSpan left, DateTimeOffset until) => left switch
    {
        { TotalMinutes: < 1 } => "<1m left",
        { TotalMinutes: < 60 } => $"{(int)left.TotalMinutes}m left",
        { TotalHours: < 48 } => $"{(int)left.TotalHours}h {left.Minutes}m left",
        _ => "until " + until.ToLocalTime().ToString("ddd HH:mm", CultureInfo.InvariantCulture),
    };
}

public static class SnoozeTimes
{
    public static (DateTimeOffset? Until, bool UntilChange) For(SnoozeChoice choice, DateTimeOffset now, WorkHoursConfig wh) => choice switch
    {
        SnoozeChoice.OneHour => (now.AddHours(1), false),
        SnoozeChoice.FourHours => (now.AddHours(4), false),
        SnoozeChoice.Tomorrow => (At(WorkHours.NextStart(wh, now.Date.AddDays(1)), now), false),
        SnoozeChoice.NextWeek => (At(WorkHours.NextStart(wh, NextMonday(now.Date)), now), false),
        _ => (null, true),
    };

    static DateTime NextMonday(DateTime d)
    {
        var days = ((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(days == 0 ? 7 : days).AddTicks(-1); // NextStart is "strictly after", so start just before Monday 00:00
    }

    /// <summary>Interprets a wall-clock time in the same offset as <paramref name="now"/>.</summary>
    internal static DateTimeOffset At(DateTime local, DateTimeOffset now) => new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), now.Offset);
}

public static class MutePatterns
{
    public static string? For(InboxItem item, MuteScope scope)
    {
        var prefix = item.Provider == Provider.GitHub ? "gh:" : "ado:";
        var seg = item.Repo.Split('/');
        return scope switch
        {
            MuteScope.Repo => prefix + item.Repo,
            MuteScope.Org => prefix + seg[0],
            MuteScope.Project when item.Provider == Provider.Ado && seg.Length >= 2 => $"{prefix}{seg[0]}/{seg[1]}",
            _ => null,
        };
    }

    public static DateTimeOffset? Until(MuteDuration d, DateTimeOffset now, WorkHoursConfig wh) => d switch
    {
        MuteDuration.OneHour => now.AddHours(1),
        MuteDuration.Tomorrow => SnoozeTimes.At(WorkHours.NextStart(wh, now.Date.AddDays(1)), now),
        _ => null,
    };
}
