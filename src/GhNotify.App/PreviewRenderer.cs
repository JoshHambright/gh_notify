using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.App;

/// <summary>
/// <c>GhNotify.exe --render-preview &lt;dir&gt;</c>: renders the popup (sample data only) and the tray icon to PNGs.
/// Runs before the single-instance mutex and never creates the controller, so it touches no real data.
/// </summary>
public static class PreviewRenderer
{
    static readonly ActionPreview ApprovalPreview = new(true, "Approve platform-release 20261013.4 in contoso/platform",
        "Approve platform-release 20261013.4", null, null, null);
    static readonly ActionPreview PrPreview = new(true, "Approve PR #482 in fabrikam/customer-platform/checkout-service-api at 9f3c2ab",
        "Approve PR #482", ActionText.NoPreReview, null, null);

    public static int Run(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var written = new List<string>();
            foreach (var (name, mode) in new[] { ("dark", ThemeMode.Dark), ("light", ThemeMode.Light) })
            {
                using var theme = new ThemeService(() => mode);
                theme.Apply();
                foreach (var (state, host) in States())
                    written.Add(RenderPopup(host, Path.Combine(dir, $"popup-{name}-{state}.png")));
                written.Add(RenderPopup(new SampleHost(), Path.Combine(dir, $"popup-{name}-menu.png"), withRowMenu: true));
            }
            foreach (var size in new[] { 16, 20, 24, 32 })
            {
                written.Add(Save(TrayIconRenderer.Render(size, null, false), Path.Combine(dir, $"tray-{size}-zero.png")));
                written.Add(Save(TrayIconRenderer.Render(size, "7", false), Path.Combine(dir, $"tray-{size}-count.png")));
                written.Add(Save(TrayIconRenderer.Render(size, "7", true), Path.Combine(dir, $"tray-{size}-attention.png")));
            }
            File.WriteAllLines(Path.Combine(dir, "preview-files.txt"), written);
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(dir, "preview-error.txt"), ex.ToString()); }
            catch (Exception writeEx) { Console.Error.WriteLine($"{ex}\n(could not write preview-error.txt: {writeEx.Message})"); }
            return 1;
        }
    }

    static IEnumerable<(string Name, SampleHost Host)> States()
    {
        var opened = DateTimeOffset.Now.AddSeconds(-1);
        yield return ("collapsed", new SampleHost());

        var expanded = new SampleHost();
        expanded.States[SampleHost.CiKey] = new RowUiState(RowMode.Expanded);
        yield return ("expanded", expanded);

        var approval = new SampleHost();
        approval.States[SampleHost.ApprovalKey] = new RowUiState(RowMode.Confirming, ItemAction.Approve, ApprovalPreview, null, opened);
        yield return ("confirm-approval", approval);

        var pr = new SampleHost();
        pr.States[SampleHost.ReviewKey] = new RowUiState(RowMode.Confirming, ItemAction.ApprovePr, PrPreview, null, opened);
        yield return ("confirm-pr-warning", pr);

        var failed = new SampleHost();
        failed.States[SampleHost.ApprovalKey] = new RowUiState(RowMode.Failed, ItemAction.Approve, ApprovalPreview,
            "You don't have permission to do this", opened);
        yield return ("failed", failed);

        yield return ("empty", new SampleHost { Empty = true });

        // Muted row, collapsed "Waiting on others", expanded "Snoozed" (the sample data's filter states).
        yield return ("filters", new SampleHost());
    }

    /// <param name="withRowMenu">Also draws the first row's ⋯ menu below its button. The menu is laid out detached
    /// (never opened), so nothing flashes on screen.</param>
    static string RenderPopup(SampleHost host, string path, bool withRowMenu = false)
    {
        var w = new PopupWindow(host) { ShowActivated = false, Left = -10000, Top = 0, MaxHeight = 1400 };
        try
        {
            w.Show();
            w.Render();
            Pump();
            w.UpdateLayout();
            Pump();
            var frame = w.Frame;
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth * 2), (int)Math.Ceiling(frame.ActualHeight * 2),
                192, 192, PixelFormats.Pbgra32);
            bmp.Render(frame);
            if (withRowMenu) DrawRowMenu(w, host, bmp);
            return Save(bmp, path);
        }
        finally
        {
            w.Close();
        }
    }

    static void DrawRowMenu(PopupWindow w, SampleHost host, RenderTargetBitmap bmp)
    {
        var key = host.BuildSections().SelectMany(s => s.Rows).First().Key;
        var button = Descendants(w.Frame).OfType<Button>().First(b => b.Tag as string == key && "Snooze or mute".Equals(b.ToolTip));
        var menu = w.RowMenuFor(button, key) ?? throw new InvalidOperationException("no row menu for " + key);
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        Pump();
        // Bottom-right of the menu under the button's bottom-right, like a right-aligned dropdown.
        var corner = button.TransformToAncestor(w.Frame).Transform(new Point(button.ActualWidth, button.ActualHeight));
        var layer = new DrawingVisual();
        using (var dc = layer.RenderOpen())
            dc.DrawRectangle(new VisualBrush(menu), null, new Rect(new Point(corner.X - menu.ActualWidth, corner.Y + 2), menu.RenderSize));
        bmp.Render(layer);
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>Lets layout, data binding and DynamicResource lookups settle.</summary>
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    static string Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }
}
