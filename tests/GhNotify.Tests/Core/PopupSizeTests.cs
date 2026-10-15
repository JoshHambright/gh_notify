using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PopupSizeTests
{
    [Theory]
    [InlineData(300, 376)]
    [InlineData(500.4, 500)]
    [InlineData(5000, 760)]
    [InlineData(double.NaN, 376)]
    public void ClampWidth_keeps_the_width_between_the_default_and_the_max(double width, double expected) =>
        Assert.Equal(expected, PopupSize.ClampWidth(width));

    [Fact]
    public void ClampWidth_also_fits_the_screen_but_never_below_the_default()
    {
        Assert.Equal(600, PopupSize.ClampWidth(700, fits: 600));
        Assert.Equal(376, PopupSize.ClampWidth(700, fits: 200)); // a tiny screen still gets the default
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.2, 1.15)]
    [InlineData(3, 1.3)]
    [InlineData(0, 0.9)]
    [InlineData(double.NaN, 1.0)]
    public void NormalizeScale_snaps_to_a_supported_size(double scale, double expected) =>
        Assert.Equal(expected, PopupSize.NormalizeScale(scale));

    [Fact]
    public void RepoChars_grows_with_the_width()
    {
        Assert.Equal(34, PopupSize.RepoChars(34, PopupSize.DefaultWidth));
        Assert.Equal(34 + 20, PopupSize.RepoChars(34, PopupSize.DefaultWidth + 120));
        Assert.Equal(34, PopupSize.RepoChars(34, 100)); // narrower than the default is clamped
        Assert.Equal(int.MaxValue, PopupSize.RepoChars(int.MaxValue, PopupSize.MaxWidth)); // display.maxRepoChars has no upper bound
    }
}
