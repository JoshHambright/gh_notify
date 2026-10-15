using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using GhNotify.Core;
using SkiaSharp;

namespace GhNotify.UI.Tests;

/// <summary>Renders the popup and the settings pages from sample data (placeholder names only) to the docs/images file names.</summary>
[Trait("Category", "Screenshots")]
public class ScreenshotTests
{
    const double Scale = 2; // docs images are rendered at 2x for sharp text
    const double MaxPopupHeight = 1200;

    internal static string? OutDir => Environment.GetEnvironmentVariable("GHNOTIFY_SCREENSHOTS");

    static readonly ActionPreview ApprovalPreview = new(true, "Approve platform-release 20261013.4 in contoso/platform",
        "Approve platform-release 20261013.4", null, null, null);

    [AvaloniaFact]
    public void Render_docs_images()
    {
        var dir = OutDir;
        if (string.IsNullOrWhiteSpace(dir)) return; // a no-op unless GHNOTIFY_SCREENSHOTS=<outdir> (build/screenshots.ps1 sets it)
        Directory.CreateDirectory(dir);
        var opened = DateTimeOffset.Now.AddSeconds(-1);

        var expanded = new SampleHost();
        expanded.States[SampleHost.CiKey] = new RowUiState(RowMode.Expanded);
        var approval = new SampleHost();
        approval.States[SampleHost.ApprovalKey] = new RowUiState(RowMode.Confirming, ItemAction.Approve, ApprovalPreview, null, opened);

        Popup(ThemeVariant.Dark, expanded, Path.Combine(dir, "hero-dark.png"));
        Popup(ThemeVariant.Dark, new SampleHost(), Path.Combine(dir, "popup-dark.png"));
        Popup(ThemeVariant.Light, new SampleHost(), Path.Combine(dir, "popup-light.png"));
        Popup(ThemeVariant.Dark, approval, Path.Combine(dir, "approve.png"));
        Popup(ThemeVariant.Dark, new SampleHost(), Path.Combine(dir, "filters.png"));
        Settings(ThemeVariant.Dark, SettingsPage.Workflows, Path.Combine(dir, "settings-workflows.png"));
        Settings(ThemeVariant.Light, SettingsPage.Notifications, Path.Combine(dir, "settings-notifications.png"));

        Save(TrayIconRenderer.Render(32, null, false), Path.Combine(dir, "tray-calm.png"));
        Save(TrayIconRenderer.Render(32, "7", true), Path.Combine(dir, "tray-waiting.png"));

        // Every popup state and settings page in both themes, for the parity review (build/screenshots.ps1 deletes this folder).
        var review = Path.Combine(dir, "review");
        Directory.CreateDirectory(review);
        foreach (var (name, variant) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            Popup(variant, new SampleHost(), Path.Combine(review, $"popup-{name}-collapsed.png"));
            Popup(variant, new SampleHost { Empty = true }, Path.Combine(review, $"popup-{name}-empty.png"));
            foreach (var page in Enum.GetValues<SettingsPage>())
                Settings(variant, page, Path.Combine(review, $"settings-{page.ToString().ToLowerInvariant()}-{name}.png"));
        }
    }

    static void Popup(ThemeVariant variant, SampleHost host, string path)
    {
        Application.Current!.RequestedThemeVariant = variant;
        Capture(new PopupWindow(host) { ShowActivated = false }, path);
    }

    static void Settings(ThemeVariant variant, SettingsPage page, string path)
    {
        Application.Current!.RequestedThemeVariant = variant;
        var w = new SettingsWindow(new SampleSettingsHost()) { ShowActivated = false, Width = 880, Height = 620 };
        // The placeholder window has no page switcher yet; the ported window exposes ShowPage(SettingsPage).
        w.GetType().GetMethod("ShowPage", [typeof(SettingsPage)])?.Invoke(w, [page]);
        Capture(w, path);
    }

    static void Capture(Window w, string path)
    {
        try
        {
            w.Show();
            if (w is PopupWindow p)
            {
                p.Render();
                Dispatcher.UIThread.RunJobs();
                FitToContent(p);
                p.ClearSelection(); // no keyboard selection outline in docs images
            }
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var size = new PixelSize((int)Math.Round(w.Bounds.Width * Scale), (int)Math.Round(w.Bounds.Height * Scale));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * Scale, 96 * Scale));
            bmp.Render(w);
            bmp.Save(path);
        }
        finally
        {
            w.Close();
        }
    }

    /// <summary>Sizes the popup to its content (the row list scrolls, so SizeToContent alone clips), capped at a reasonable height.</summary>
    static void FitToContent(PopupWindow p)
    {
        var scroller = p.FindControl<ScrollViewer>("Scroller") ?? throw new InvalidOperationException("no Scroller");
        var chrome = p.Bounds.Height - scroller.Viewport.Height;
        var want = Math.Min(scroller.Extent.Height + chrome, MaxPopupHeight);
        p.SizeToContent = SizeToContent.Manual;
        p.MaxHeight = double.PositiveInfinity;
        p.Height = Math.Max(p.MinHeight, want);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    static void Save(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
