using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ThemeAndBadgeTests
{
    [Theory]
    [InlineData(ThemeMode.Dark, 1, true)]
    [InlineData(ThemeMode.Light, 0, false)]
    [InlineData(ThemeMode.System, 0, true)]
    [InlineData(ThemeMode.System, 1, false)]
    [InlineData(ThemeMode.System, null, false)]
    public void Resolves_dark(ThemeMode mode, int? appsUseLight, bool dark) =>
        Assert.Equal(dark, ThemeResolver.IsDark(mode, appsUseLight));

    [Fact]
    public void Cycles_modes()
    {
        Assert.Equal(ThemeMode.Dark, ThemeResolver.Next(ThemeMode.System));
        Assert.Equal(ThemeMode.Light, ThemeResolver.Next(ThemeMode.Dark));
        Assert.Equal(ThemeMode.System, ThemeResolver.Next(ThemeMode.Light));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void Badge(int count, string? expected) => Assert.Equal(expected, BadgeText.For(count));
}
