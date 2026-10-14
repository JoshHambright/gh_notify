namespace GhNotify.Core;

public static class ItemText
{
    /// <summary>"acme/api #57" when the item has a PR number, else just the repo.</summary>
    public static string RepoAndNumber(InboxItem item) => item.Number is { } n ? $"{item.Repo} #{n}" : item.Repo;
}
