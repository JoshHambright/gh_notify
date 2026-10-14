using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public static class GitHubPrMapper
{
    /// <exception cref="JsonException">data.review is missing or not an object.</exception>
    public static IEnumerable<InboxItem> ReviewRequests(JsonElement data) => ReviewRequests(data, Section(data, "review"));

    /// <exception cref="JsonException">data.mine is missing or not an object.</exception>
    public static IEnumerable<InboxItem> MyPrs(JsonElement data, bool includeDrafts = true) => MyPrs(data, Section(data, "mine"), includeDrafts);

    // A missing search result is an error, not "no items" (it would remove every item of that kind).
    static JsonElement Section(JsonElement data, string name)
    {
        var section = data.Obj(name);
        if (section.ValueKind != JsonValueKind.Object) throw new JsonException($"GitHub GraphQL: data.{name} is missing");
        return section;
    }

    static IEnumerable<InboxItem> ReviewRequests(JsonElement data, JsonElement review)
    {
        var me = data.Obj("viewer").Str("login");
        foreach (var node in review.Arr("nodes"))
        {
            if (node.Int("number") is null) continue;
            var item = Base(node, ItemKind.ReviewRequested, "gh:review", null);
            if (!string.Equals(item.Author, me, StringComparison.OrdinalIgnoreCase)) yield return item;
        }
    }

    static IEnumerable<InboxItem> MyPrs(JsonElement data, JsonElement mine, bool includeDrafts)
    {
        var me = data.Obj("viewer").Str("login");
        foreach (var node in mine.Arr("nodes"))
        {
            if (node.Int("number") is null) continue;
            var state = node.Obj("commits").Arr("nodes").Select(n => n.Obj("commit").Obj("statusCheckRollup").Str("state")).FirstOrDefault();
            var failing = state is "FAILURE" or "ERROR";
            if (failing)
                yield return Base(node, ItemKind.MyPrCiFailing, "gh:cifail", "Checks failing");

            var parts = new List<string>();
            if (node.Str("reviewDecision") == "CHANGES_REQUESTED") parts.Add("Changes requested");
            var unresolved = node.Obj("reviewThreads").Arr("nodes").Count(t =>
                !t.Bool("isResolved") &&
                !string.Equals(t.Obj("comments").Arr("nodes").LastOrDefault().Obj("author").Str("login"), me, StringComparison.OrdinalIgnoreCase));
            if (unresolved > 0) parts.Add($"{unresolved} unresolved thread{(unresolved == 1 ? "" : "s")}");
            if (parts.Count > 0)
                yield return Base(node, ItemKind.MyPrNeedsResponse, "gh:respond", string.Join(", ", parts));
            if (failing || parts.Count > 0) continue;

            var draft = node.Bool("isDraft");
            var decision = node.Str("reviewDecision");
            var merge = node.Str("mergeStateStatus");
            if (!draft && decision == "APPROVED" && state == "SUCCESS" && merge is "CLEAN" or "HAS_HOOKS")
            {
                yield return Base(node, ItemKind.MyPrReadyToMerge, "gh:ready", "Approved · checks green");
                continue;
            }
            if (draft && !includeDrafts) continue;
            yield return Base(node, ItemKind.MyPrWaiting, "gh:waiting", WaitingDetail(draft, decision, state, merge));
        }
    }

    static string WaitingDetail(bool draft, string? decision, string? rollup, string? merge) =>
        draft ? "Draft"
        : merge == "DIRTY" ? "Merge conflicts"
        : rollup is "PENDING" or "EXPECTED" ? "Checks running"
        : decision != "APPROVED" ? "Waiting on review"
        : merge == "BEHIND" ? "Behind base branch"
        : "Not mergeable yet";

    static InboxItem Base(JsonElement n, ItemKind kind, string prefix, string? detail)
    {
        var repo = n.Obj("repository").Str("nameWithOwner") ?? "?";
        var number = n.Int("number")!.Value;
        var author = n.Obj("author");
        var login = author.Str("login") ?? "ghost";
        if (author.Str("__typename") == "Bot" && !login.EndsWith("[bot]", StringComparison.Ordinal)) login += "[bot]";
        int? lines = n.Int("additions") is { } a && n.Int("deletions") is { } d ? a + d : null;
        return new InboxItem($"{prefix}:{repo}#{number}", Provider.GitHub, kind, repo, n.Str("title") ?? "", n.Str("url") ?? "",
            login, n.Date("updatedAt"), n.Str("headRefOid"), lines, detail)
        {
            Number = number,
            SourceRef = $"pull/{number}/head",
            CloneUrl = n.Obj("repository").Str("url"),
            Details = DetailList.Of(("Repo", repo), ("Author", login),
                ("Branch", n.Str("headRefName") is { } h ? $"{h} → {n.Str("baseRefName")}" : null),
                ("Commit", DetailList.Short(n.Str("headRefOid"))), ("Created", DetailList.Local(n.Date("createdAt")))),
        };
    }
}
