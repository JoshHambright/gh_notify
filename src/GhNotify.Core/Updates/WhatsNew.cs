using System.Text.RegularExpressions;

namespace GhNotify.Core;

/// <summary>
/// The "Updated to vX · What's new" footer link and the tray's What's new item: which version to announce after an update,
/// and the release page to open. Release notes live in docs/releases/vX.Y.Z.md and are published on the GitHub release.
/// </summary>
public static partial class WhatsNew
{
    public const string ReleasesUrl = "https://github.com/JoshHambright/gh_notify/releases";

    /// <summary>The release page for <paramref name="version"/>, or the releases list for a dev or unknown version.</summary>
    public static string UrlFor(string? version) => IsRelease(version) ? $"{ReleasesUrl}/tag/v{version}" : ReleasesUrl;

    public static bool IsRelease(string? version) => version is not null && ReleaseVersion().IsMatch(version);

    /// <summary>
    /// Run at startup of an installed build. Returns the version to announce (null = none) and the version to remember as last run.
    /// A changed version announces itself; so does the first run of a build that remembers no version but has used the app before
    /// (an upgrade from a release older than this feature). A fresh install announces nothing. An unacknowledged announcement is kept.
    /// </summary>
    public static (string? Announce, string? LastRun) OnStart(string? lastRun, string? pending, string current, bool usedBefore)
    {
        if (!IsRelease(current) || lastRun == current) return (pending, lastRun);
        var upgraded = lastRun is not null || usedBefore;
        return (upgraded ? current : pending, current);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ReleaseVersion();
}
