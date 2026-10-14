using GhNotify.Core;

namespace GhNotify.App;

/// <summary>Pure text/state rules for the popup's update footer link and ⋯ menu items.</summary>
public static class UpdateUi
{
    public static string VersionHeader(string version) => $"gh_notify v{version}";

    /// <summary>The red footer link text, or null when no update is ready.</summary>
    public static string? FooterLink(UpdateStatus s) =>
        s.Phase == UpdatePhase.Ready ? $"Update ready · v{s.ReadyVersion} · Restart" : null;

    /// <summary>The inline confirm prompt shown after a restart was refused because agents are running.</summary>
    public static string ConfirmPrompt(int agents) => $"{agents} agent{(agents == 1 ? "" : "s")} running — ";

    public const string RestartAnyway = "Restart anyway";
    public const string Cancel = "Cancel";

    /// <summary>"Check for updates": disabled in dev builds (with a tooltip) and while a check or download is running.</summary>
    public static (string Header, bool Enabled, string? Tooltip) CheckItem(UpdateStatus s) => s.Phase switch
    {
        UpdatePhase.DevBuild => ("Check for updates", false, "Updates are off in dev builds"),
        UpdatePhase.Checking => ("Checking for updates…", false, null),
        UpdatePhase.Downloading => ("Downloading update…", false, null),
        _ => ("Check for updates", true, null),
    };

    /// <summary>"Restart to update (vX)" when an update is ready, otherwise null.</summary>
    public static string? RestartItem(UpdateStatus s) =>
        s.Phase == UpdatePhase.Ready ? $"Restart to update (v{s.ReadyVersion})" : null;
}
