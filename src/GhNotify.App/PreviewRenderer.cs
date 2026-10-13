using System.Windows;
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
    }

    static string RenderPopup(SampleHost host, string path)
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
            return Save(bmp, path);
        }
        finally
        {
            w.Close();
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
