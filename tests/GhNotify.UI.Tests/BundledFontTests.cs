using Avalonia.Headless.XUnit;
using Avalonia.Platform;

namespace GhNotify.UI.Tests;

public class BundledFontTests
{
    [AvaloniaTheory]
    [InlineData("Regular")]
    [InlineData("Medium")]
    [InlineData("Bold")]
    [InlineData("Black")]
    public void Roboto_weights_are_embedded_as_avalonia_resources(string weight)
    {
        var uri = new Uri($"avares://GhNotify.UI/Fonts/Roboto-{weight}.ttf");
        Assert.True(AssetLoader.Exists(uri));
        using var stream = AssetLoader.Open(uri);
        Assert.True(stream.Length > 10_000);
    }
}
