using System.Windows;
using System.Windows.Media;

namespace GhNotify.App.Tests;

public class BundledFontTests
{
    [Fact]
    public void Roboto_Regular_Bold_and_Black_resolve_from_the_embedded_resources()
    {
        var uris = new Dictionary<string, string>(); Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                var family = new FontFamily(new Uri("pack://application:,,,/GhNotify;component/Fonts/"), "./#Roboto");
                foreach (var (name, weight) in new[] { ("Regular", FontWeights.Normal), ("Bold", FontWeights.Bold), ("Black", FontWeights.Black) })
                {
                    var ok = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var g);
                    uris[name] = ok ? g.FontUri.ToString() : "unresolved";
                }
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw err;
        foreach (var name in new[] { "Regular", "Bold", "Black" })
        {
            Assert.Contains($"Roboto-{name}", uris[name], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GhNotify", uris[name], StringComparison.OrdinalIgnoreCase);
        }
    }
}
