using GhNotify.Core;
using GhNotify.UI;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GhNotify.Platform.Windows;

/// <summary>Windows toast notifications (Microsoft.Toolkit.Uwp.Notifications). Create it once, early: it hooks toast activation, including a launch caused by a toast click.</summary>
public sealed class ToastNotifier : INotifier
{
    const string Group = "ghnotify";
    volatile string? _logoPath;

    public ToastNotifier() => ToastNotificationManagerCompat.OnActivated += OnActivated;

    public event Action<string, string>? Activated;

    /// <summary>Writes the toast app logo (the accent tile) to <paramref name="path"/> and uses it for later toasts.</summary>
    public void PrepareLogo(string path)
    {
        try
        {
            using var bmp = TrayIconRenderer.RenderAccent(64);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, TrayIconRenderer.ToPng(bmp));
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
            builder.AddArgument("tag", t.Tag);
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

    public void ClearAll()
    {
        try { ToastNotificationManagerCompat.History.Clear(); }
        catch (Exception ex) { Log.Error("could not clear toasts", ex); }
    }

    /// <summary>Maps toast arguments to the shared action string; internal for tests.</summary>
    internal static (string Tag, string Action) Translate(string argument)
    {
        var args = ToastArguments.Parse(argument);
        args.TryGetValue("tag", out var tag);
        var action = args.TryGetValue("url", out var url) ? ToastActions.ForUrl(url) : ToastActions.Open;
        return (tag ?? "", action);
    }

    void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var (tag, action) = Translate(e.Argument);
            Activated?.Invoke(tag, action);
        }
        catch (Exception ex)
        {
            Log.Error("toast activation failed", ex);
        }
    }
}
