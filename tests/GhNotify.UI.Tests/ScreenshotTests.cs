using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using GhNotify.Core;
using SkiaSharp;

namespace GhNotify.UI.Tests;

/// <summary>Renders the popup and the settings pages from sample data (placeholder names only) to the docs/images file names.</summary>
[Trait("Category", "Screenshots")]
public class ScreenshotTests
{
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
            if (w is PopupWindow p) p.Render();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = w.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame rendered for " + path);
            frame.Save(path);
        }
        finally
        {
            w.Close();
        }
    }

    static void Save(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
