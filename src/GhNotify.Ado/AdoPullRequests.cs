using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public static class AdoPullRequests
{
    public const string BuildPolicyTypeId = "0609b952-1397-4640-95ec-e00a01b2c241";

    public static string ProjectUrl(string org, AdoProject p) => $"https://dev.azure.com/{org}/{Uri.EscapeDataString(p.Name)}";

    public static async Task<IReadOnlyList<InboxItem>> FetchAsync(
        AdoClient c, string org, AdoProject p, AdoOrgContext ctx, bool includeDrafts, CancellationToken ct)
    {
        var baseUrl = ProjectUrl(org, p);
        var prs = await c.GetAsync($"{baseUrl}/_apis/git/pullrequests?searchCriteria.status=active&$top=1000&api-version=7.1", ct);
        var items = new List<InboxItem>();
        foreach (var pr in prs.Arr("value"))
        {
            if (string.Equals(pr.Obj("createdBy").Str("id"), ctx.MyId, StringComparison.OrdinalIgnoreCase))
                items.AddRange(await MineAsync(c, org, p, baseUrl, ctx, pr, ct));
            else if (ReviewWaiting(pr, ctx, includeDrafts))
                items.Add(Map(org, p, pr, ItemKind.ReviewRequested, "ado:review", null));
        }
        return items;
    }

    public static bool ReviewWaiting(JsonElement pr, AdoOrgContext ctx, bool includeDrafts)
    {
        if (!includeDrafts && pr.Bool("isDraft")) return false;
        var reviewers = pr.Arr("reviewers").ToList();
        var mine = reviewers.Where(r => string.Equals(r.Str("id"), ctx.MyId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count > 0) return mine.Any(Pending);
        return reviewers.Any(r => r.Str("id") is { } id && ctx.MyIdentityIds.Contains(id) && Pending(r));
    }

    static bool Pending(JsonElement reviewer) => (reviewer.Int("vote") ?? 0) == 0 && !reviewer.Bool("hasDeclined");

    static async Task<IReadOnlyList<InboxItem>> MineAsync(
        AdoClient c, string org, AdoProject p, string baseUrl, AdoOrgContext ctx, JsonElement pr, CancellationToken ct)
    {
        var list = new List<InboxItem>();
        var prId = pr.Int("pullRequestId");
        var repoId = pr.Obj("repository").Str("id");

        var artifact = Uri.EscapeDataString($"vstfs:///CodeReview/CodeReviewId/{p.Id}/{prId}");
        var evals = await c.GetAsync($"{baseUrl}/_apis/policy/evaluations?artifactId={artifact}&api-version=7.1-preview.1", ct);
        var failing = evals.Arr("value").Count(e =>
            e.Str("status") is "rejected" or "broken" &&
            string.Equals(e.Obj("configuration").Obj("type").Str("id"), BuildPolicyTypeId, StringComparison.OrdinalIgnoreCase));
        if (failing > 0)
            list.Add(Map(org, p, pr, ItemKind.MyPrCiFailing, "ado:cifail", $"{failing} build{(failing == 1 ? "" : "s")} failing"));

        var parts = new List<string>();
        var others = pr.Arr("reviewers").Where(r => !string.Equals(r.Str("id"), ctx.MyId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Any(r => r.Int("vote") == -10)) parts.Add("Rejected");
        else if (others.Any(r => r.Int("vote") == -5)) parts.Add("Waiting for author");

        var threads = await c.GetAsync($"{baseUrl}/_apis/git/repositories/{repoId}/pullRequests/{prId}/threads?api-version=7.1", ct);
        var open = threads.Arr("value").Count(t =>
            t.Str("status") == "active" && !t.Bool("isDeleted") &&
            t.Arr("comments").Where(cm => cm.Str("commentType") != "system" && !cm.Bool("isDeleted")).LastOrDefault()
                .Obj("author").Str("id") is { } author &&
            !string.Equals(author, ctx.MyId, StringComparison.OrdinalIgnoreCase));
        if (open > 0) parts.Add($"{open} active thread{(open == 1 ? "" : "s")}");

        if (parts.Count > 0)
            list.Add(Map(org, p, pr, ItemKind.MyPrNeedsResponse, "ado:respond", string.Join(", ", parts)));
        return list;
    }

    public static InboxItem Map(string org, AdoProject p, JsonElement pr, ItemKind kind, string prefix, string? detail)
    {
        var repoName = pr.Obj("repository").Str("name") ?? "?";
        var id = pr.Int("pullRequestId") ?? 0;
        var repoPath = $"{org}/{p.Name}/{repoName}";
        var web = $"{ProjectUrl(org, p)}/_git/{Uri.EscapeDataString(repoName)}";
        return new InboxItem($"{prefix}:{repoPath}#{id}", Provider.Ado, kind, repoPath, pr.Str("title") ?? "", $"{web}/pullrequest/{id}",
            pr.Obj("createdBy").Str("displayName") ?? "", pr.Date("creationDate"), pr.Obj("lastMergeSourceCommit").Str("commitId"), null, detail)
        {
            Number = id,
            SourceRef = pr.Str("sourceRefName"),
            CloneUrl = web,
        };
    }
}
