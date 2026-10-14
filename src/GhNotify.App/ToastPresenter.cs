using System.Windows.Media.Imaging;
using GhNotify.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GhNotify.App;

public sealed class ToastPresenter
{
    const string Group = "ghnotify";
    static volatile string? _logoPath;

    /// <summary>Writes the toast app logo (the B1 tile) to %LOCALAPPDATA%\GhNotify\logo.png. Call on the UI thread at startup.</summary>
    public static void PrepareLogo(string path)
    {
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(TrayIconRenderer.RenderAccent(64)));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var fs = File.Create(path)) enc.Save(fs);
            _logoPath = path;
        }
        catch (Exception ex)
        {
            Log.Error("could not write toast logo", ex);
        }
    }

    public void Apply(ToastRequest t)
    {
        try
        {
            if (t.Action == ToastAction.Remove)
            {
                ToastNotificationManagerCompat.History.Remove(t.Tag, Group);
                return;
            }
            var builder = new ToastContentBuilder().AddText(t.Title ?? "gh_notify");
            if (t.Silent) builder.AddAudio(new ToastAudio { Silent = true });
            if (!string.IsNullOrEmpty(t.Body)) builder.AddText(t.Body);
            if (_logoPath is { } logo) builder.AddAppLogoOverride(new Uri(logo));
            if (t.Url is not null) builder.AddArgument("url", t.Url);
            else builder.AddArgument("action", "open");
            builder.Show(toast =>
            {
                toast.Tag = t.Tag;
                toast.Group = Group;
            });
        }
        catch (Exception ex)
        {
            Log.Error($"toast {t.Action} {t.Tag} failed", ex);
        }
    }
}
