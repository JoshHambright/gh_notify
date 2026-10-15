using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>The popup width (dragged from the left edge, saved by the host) and the text size (scales the whole popup).</summary>
public class PopupSizeTests
{
    static string RepoText(PopupHarness h, string key) =>
        h.All<TextBlock>(h.Card(key)).First(t => ToolTip.GetTip(t) is string tip && tip.Contains('/')).Inlines!.OfType<Avalonia.Controls.Documents.Run>().Aggregate("", (s, r) => s + r.Text);

    [AvaloniaFact]
    public void Dragging_the_left_edge_widens_the_popup_and_saves_the_width_on_release()
    {
        using var h = new PopupHarness();
        Assert.Equal(PopupSize.DefaultWidth, h.Window.Width);

        h.Window.BeginWidthDrag(1000);
        h.Window.DragWidthTo(900); // 100 px to the left
        Assert.Equal(476, h.Window.Width);
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("width", StringComparison.Ordinal)); // saved once, on release

        h.Window.DragWidthTo(-5000);
        Assert.Equal(PopupSize.MaxWidth, h.Window.Width);
        h.Window.DragWidthTo(950);
        h.Window.EndWidthDrag();
        h.Pump();
        Assert.Contains("width 426", h.Host.Calls);
        Assert.Equal(426, h.Window.Width);
    }

    [AvaloniaFact]
    public void A_refresh_mid_drag_does_not_snap_the_edge_back()
    {
        using var h = new PopupHarness();
        h.Window.BeginWidthDrag(1000);
        h.Window.DragWidthTo(800);
        h.Host.Raise();
        h.Pump();
        Assert.Equal(576, h.Window.Width);
    }

    [AvaloniaFact]
    public void A_wider_popup_shows_longer_repo_names()
    {
        using (var narrow = new PopupHarness())
            Assert.Contains(narrow.CardKeys(), k => RepoText(narrow, k).Contains('…')); // fabrikam/customer-platform/checkout-service-api
        using var h = new PopupHarness(s => s.PopupWidth = PopupSize.MaxWidth);
        Assert.Equal(PopupSize.MaxWidth, h.Window.Width);
        Assert.All(h.CardKeys(), k => Assert.DoesNotContain("…", RepoText(h, k)));
    }

    [AvaloniaFact]
    public void Text_size_scales_the_whole_popup_and_its_width()
    {
        using var h = new PopupHarness(s => { s.TextScale = 1.3; s.PopupWidth = 400; });
        Assert.Equal(520, h.Window.Width);
        var scale = Assert.IsType<ScaleTransform>(h.Window.FindControl<LayoutTransformControl>("Scaler")!.LayoutTransform);
        Assert.Equal(1.3, scale.ScaleX);

        // Dragging at 130%: 130 screen px is 100 unscaled px.
        h.Window.BeginWidthDrag(1000);
        h.Window.DragWidthTo(870);
        h.Window.EndWidthDrag();
        Assert.Contains("width 500", h.Host.Calls);

        h.Host.TextScale = 1.0;
        h.Host.Raise();
        h.Pump();
        Assert.Null(h.Window.FindControl<LayoutTransformControl>("Scaler")!.LayoutTransform);
        Assert.Equal(500, h.Window.Width);
    }
}
