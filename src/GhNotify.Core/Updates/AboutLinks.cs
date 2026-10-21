namespace GhNotify.Core;

/// <summary>The About menu's links: the repo, and a new-issue page pre-filled with the version and OS (nothing else about the machine or account).</summary>
public static class AboutLinks
{
    public const string RepoUrl = "https://github.com/JoshHambright/gh_notify";

    public const string RepoItem = "GitHub repo (star it)";
    public const string IssueItem = "Report an issue";
    public const string Header = "About gh_notify";

    /// <summary>The new-issue URL with a body that leaves room to describe the problem, then the version and OS.</summary>
    public static string IssueUrl(string version, string os) =>
        $"{RepoUrl}/issues/new?body={Uri.EscapeDataString($"\n\n---\ngh_notify {version}, {os}")}";
}
