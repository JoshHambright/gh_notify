using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Shows and removes OS notifications. <see cref="Activated"/> fires with the toast tag and a <see cref="ToastActions"/> action string.</summary>
public interface INotifier
{
    void Apply(ToastRequest request);
    event Action<string /*tag*/, string /*action*/>? Activated;
    void ClearAll();
}

/// <summary>A global hotkey. One registration at a time: <see cref="TryRegister"/> replaces any earlier one.</summary>
public interface IHotkeyService
{
    /// <summary>False (with the reason in <paramref name="error"/>) when the OS refuses, e.g. another app owns the combination.</summary>
    bool TryRegister(Hotkey hotkey, Action onPressed, out string? error);
    void Unregister();
}

public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
    /// <summary>Why start-at-login is not in effect, for Settings to show (macOS: the login item needs approval); null when fine.</summary>
    string? Problem => null;
}

public interface ISoundService
{
    /// <summary>Plays "builtin:&lt;name&gt;" or a .wav path asynchronously; falls back to the system notification sound. Never throws.</summary>
    void Play(string soundIdOrPath);
}

public interface ISystemTheme
{
    bool IsDark { get; }
    /// <summary>Raised (on any thread) when the OS light/dark setting changes.</summary>
    event Action? Changed;
}

public interface IShell
{
    /// <summary>Opens an item URL in the default browser. https only (<see cref="ItemUrl.IsOpenable"/>): anything else is refused.</summary>
    void OpenUrl(string httpsUrl);
    /// <summary>Opens one of our own local paths (a folder, or a file such as config.json) with the OS default handler.</summary>
    void OpenFolder(string path);
    /// <summary>Opens a terminal in <paramref name="workingDir"/> running <paramref name="command"/> (see <c>GhNotify.Agents.OpenTerminal</c>).</summary>
    void OpenTerminal(string workingDir, string command);
}

public sealed record PlatformServices(INotifier Notifier, IHotkeyService Hotkey, IStartupService Startup, ISoundService Sound, ISystemTheme Theme, IShell Shell);

/// <summary>
/// The <c>action</c> string of <see cref="INotifier.Activated"/>: <see cref="Open"/> (show the popup) or <c>url:&lt;https url&gt;</c>
/// (open that item URL). Shared so every <see cref="INotifier"/> reports activation the same way.
/// </summary>
public static class ToastActions
{
    public const string Open = "open";
    const string UrlPrefix = "url:";

    public static string ForUrl(string url) => UrlPrefix + url;

    public static bool TryGetUrl(string action, out string url)
    {
        if (action.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            url = action[UrlPrefix.Length..];
            return true;
        }
        url = "";
        return false;
    }
}
