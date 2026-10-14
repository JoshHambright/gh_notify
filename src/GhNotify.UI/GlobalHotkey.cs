using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>
/// The global hotkey that toggles the popup (v1.3 spec §6) on top of the platform's <see cref="IHotkeyService"/>. Registration never
/// throws: a clash or any other failure is logged once per combination and exposed as <see cref="Error"/> for Settings → General.
/// There is no toast and startup is never blocked.
/// </summary>
public sealed class GlobalHotkey(IHotkeyService service, Action toggle, Action<string>? logError = null)
{
    readonly Action<string> _log = logError ?? (m => Log.Error(m));
    readonly HashSet<string> _logged = [];

    /// <summary>"Win+Shift+G is in use by another app — pick another", or null when registered / off.</summary>
    public string? Error { get; private set; }

    /// <summary>The combination that is currently registered, or null.</summary>
    public Hotkey? Registered { get; private set; }

    /// <summary>Registers the configured hotkey (re-registers after a config change). "" turns it off.</summary>
    public void Apply(string? text)
    {
        try { service.Unregister(); }
        catch (Exception ex) { LogOnce("unregister", $"could not release the global hotkey: {ex.Message}"); }
        Error = null;
        Registered = null;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!HotkeyParser.TryParse(text, out var hotkey, out var problem))
        {
            Error = $"Hotkey {problem}";
            LogOnce(text, $"invalid hotkey '{text}' in config: {problem}");
            return;
        }
        var shown = text.Trim(); // as the user wrote it (e.g. "Win+Shift+G"), not the canonical modifier order
        bool ok;
        string? reason = null;
        try { ok = service.TryRegister(hotkey, OnPressed, out reason); }
        catch (Exception ex)
        {
            ok = false;
            LogOnce(hotkey.ToString(), $"global hotkey {hotkey} failed: {ex.Message}");
        }
        if (ok)
        {
            Registered = hotkey;
            return;
        }
        Error = $"{shown} is in use by another app — pick another";
        LogOnce(hotkey.ToString(), $"global hotkey {shown} is in use by another app" + (reason is null ? "" : $" ({reason})"));
    }

    void OnPressed()
    {
        try { toggle(); }
        catch (Exception ex) { _log($"global hotkey handler failed: {ex}"); }
    }

    void LogOnce(string key, string message)
    {
        if (_logged.Add(key)) _log(message);
    }
}
