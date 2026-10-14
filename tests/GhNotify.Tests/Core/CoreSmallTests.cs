using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class SnoozeTextTests
{
    [Theory]
    [InlineData(0, "<1m left")]
    [InlineData(5, "5m left")]
    [InlineData(150, "2h 30m left")]
    public void Left_formats_short_spans(int minutes, string expected) =>
        Assert.Equal(expected, Snoozes.Left(TimeSpan.FromMinutes(minutes), DateTimeOffset.UnixEpoch));

    [Fact]
    public void Left_formats_long_spans_as_a_weekday_time()
    {
        var until = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        Assert.StartsWith("until ", Snoozes.Left(TimeSpan.FromDays(3), until));
    }

    [Fact]
    public void Remaining_without_a_deadline_waits_for_a_change() =>
        Assert.Equal("until it changes", Snoozes.Remaining(new Snooze("k", null, true), DateTimeOffset.UnixEpoch));
}

public class MutePatternTests
{
    [Fact]
    public void Until_maps_durations()
    {
        var now = new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
        var wh = new WorkHoursConfig();
        Assert.Equal(now.AddHours(1), MutePatterns.Until(MuteDuration.OneHour, now, wh));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero), MutePatterns.Until(MuteDuration.Tomorrow, now, wh));
        Assert.Null(MutePatterns.Until(MuteDuration.Forever, now, wh));
    }
}

public class NotificationTitleTests
{
    [Theory]
    [InlineData(ItemKind.ReviewRequested, "Review requested")]
    [InlineData(ItemKind.ApprovalPending, "Approval needed")]
    [InlineData(ItemKind.MyPrCiFailing, "Your PR is failing")]
    [InlineData(ItemKind.MyPrNeedsResponse, "Your PR needs a response")]
    [InlineData(ItemKind.MyPrReadyToMerge, "Ready to merge")]
    [InlineData(ItemKind.MyPrWaiting, "Waiting on others")]
    public void Title_per_kind(ItemKind kind, string title) =>
        Assert.Equal(title, NotificationPolicy.TitleFor(new InboxItem("k", Provider.GitHub, kind, "o/r", "t", "u", "a", DateTimeOffset.UnixEpoch, null, null, null)));
}

public class LogTests
{
    [Fact]
    public void Writes_to_a_dated_file_and_prunes_old_logs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ghn-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "old.log");
        File.WriteAllText(old, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        Log.Configure(dir);
        Log.Info("hello");
        Log.Error("bad", new InvalidOperationException("why"));
        Assert.False(File.Exists(old));
        var text = File.ReadAllText(Directory.GetFiles(dir, "*.log").Single());
        Assert.Contains("INFO hello", text);
        Assert.Contains("ERROR bad: System.InvalidOperationException: why", text);
    }
}
