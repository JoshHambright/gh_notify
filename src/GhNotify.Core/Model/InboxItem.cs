using System.Security.Cryptography;
using System.Text;

namespace GhNotify.Core;

public enum Provider { GitHub, Ado }

public enum ItemKind { ReviewRequested, ApprovalPending, MyPrCiFailing, MyPrNeedsResponse }

public sealed record InboxItem(
    string Key,
    Provider Provider,
    ItemKind Kind,
    string Repo,
    string Title,
    string Url,
    string Author,
    DateTimeOffset UpdatedAt,
    string? HeadSha,
    int? ChangedLines,
    string? Detail)
{
    /// <summary>PR number (GitHub) or pull request id (ADO); null for approvals.</summary>
    public int? Number { get; init; }
    /// <summary>Ref to fetch for a worktree: "pull/12/head" (GitHub) or "refs/heads/feature" (ADO).</summary>
    public string? SourceRef { get; init; }
    /// <summary>Remote URL used to find a local clone.</summary>
    public string? CloneUrl { get; init; }
}

public static class ItemTag
{
    public static string For(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

    public static string ForAgent(string key) => For("agent:" + key);
}
