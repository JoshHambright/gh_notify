using GhNotify.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GhNotify.App;

public sealed class ToastPresenter
{
    const string Group = "ghnotify";

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
            if (!string.IsNullOrEmpty(t.Body)) builder.AddText(t.Body);
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
