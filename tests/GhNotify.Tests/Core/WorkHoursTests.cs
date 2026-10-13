using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class WorkHoursTests
{
    static readonly WorkHoursConfig Weekdays = new();

    [Theory]
    [InlineData("2026-10-13 08:00", true)]   // Tue start inclusive
    [InlineData("2026-10-13 17:59", true)]
    [InlineData("2026-10-13 18:00", false)]  // end exclusive
    [InlineData("2026-10-13 07:59", false)]
    [InlineData("2026-10-17 10:00", false)]  // Saturday
    public void Daytime_window(string local, bool expected) =>
        Assert.Equal(expected, WorkHours.IsWithin(Weekdays, DateTime.Parse(local)));

    [Theory]
    [InlineData("2026-10-16 23:00", true)]   // Fri night, window opened Fri
    [InlineData("2026-10-17 05:00", true)]   // Sat early morning belongs to Fri's window
    [InlineData("2026-10-17 23:00", false)]  // Sat night: Sat not a listed day
    [InlineData("2026-10-13 12:00", false)]
    public void Overnight_window(string local, bool expected)
    {
        var cfg = new WorkHoursConfig { Start = "22:00", End = "06:00" };
        Assert.Equal(expected, WorkHours.IsWithin(cfg, DateTime.Parse(local)));
    }

    [Fact]
    public void Equal_start_and_end_means_all_day_on_listed_days()
    {
        var cfg = new WorkHoursConfig { Start = "00:00", End = "00:00" };
        Assert.True(WorkHours.IsWithin(cfg, DateTime.Parse("2026-10-13 03:00")));
        Assert.False(WorkHours.IsWithin(cfg, DateTime.Parse("2026-10-18 03:00")));
    }

    [Theory]
    [InlineData("8am", "18:00", "Mon")]
    [InlineData("08:00", "18:00", "Funday")]
    public void Validate_rejects_bad_values(string start, string end, string day) =>
        Assert.Throws<InvalidDataException>(() =>
            WorkHours.Validate(new WorkHoursConfig { Start = start, End = end, Days = [day] }));

    [Fact]
    public void Accepts_full_day_names_case_insensitively()
    {
        var cfg = new WorkHoursConfig { Days = ["tuesday"] };
        WorkHours.Validate(cfg);
        Assert.True(WorkHours.IsWithin(cfg, DateTime.Parse("2026-10-13 09:00")));
    }
}
