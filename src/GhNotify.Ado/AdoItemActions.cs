using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AdoItemActions(AdoClient client, AdoDirectory directory) : IItemActions
{
    const string ApprovalApi = "api-version=7.1-preview.1";

    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item)
    {
        if (string.IsNullOrEmpty(item.Org) || string.IsNullOrEmpty(item.Project)) return [];
        var actions = ActionText.DefaultActions(item);
        return actions.Count switch
        {
            0 => [],
            _ when actions[0] == ItemAction.ApprovePr => item.RepoId is { Length: > 0 } && item.Number is not null ? actions : [],
            _ => item.ApprovalId is { Length: > 0 } ? actions : [],
        };
    }

    static string Base(InboxItem i) => $"https://dev.azure.com/{i.Org}/{Uri.EscapeDataString(i.Project!)}";

    public async Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct)
    {
        if (!ActionsFor(item).Contains(action)) return ActionText.Refuse("Not supported for this item");
        try
        {
            return action == ItemAction.ApprovePr ? await PreviewPrAsync(item, run, ct) : await PreviewApprovalAsync(item, action, ct);
        }
        catch (HttpRequestException ex) { return ActionText.Refuse(ex.Message); }
    }

    async Task<ActionPreview> PreviewApprovalAsync(InboxItem item, ItemAction action, CancellationToken ct)
    {
        var ctx = await directory.GetOrgContextAsync(item.Org!, ct);
        var a = await client.GetAsync($"{Base(item)}/_apis/pipelines/approvals/{item.ApprovalId}?$expand=steps&{ApprovalApi}", ct);
        var mine = IsPending(a.Str("status")) && a.Arr("steps").Any(s =>
            IsPending(s.Str("status")) && s.Obj("assignedApprover").Str("id") is { } id && ctx.MyIdentityIds.Contains(id));
        if (!mine) return ActionText.Refuse("No longer waiting on you");
        var verb = action == ItemAction.Approve ? "Approve" : "Reject";
        return new ActionPreview(true, $"{verb} {item.Title} in {item.Org}/{item.Project}", $"{verb} {item.Title}", null, null, item.ApprovalId);
    }

    static bool IsPending(string? s) => string.Equals(s, "pending", StringComparison.OrdinalIgnoreCase);

    async Task<ActionPreview> PreviewPrAsync(InboxItem item, AgentRun? run, CancellationToken ct)
    {
        var pr = await client.GetAsync($"{Base(item)}/_apis/git/repositories/{item.RepoId}/pullRequests/{item.Number}?api-version=7.1", ct);
        if (!string.Equals(pr.Str("status"), "active", StringComparison.OrdinalIgnoreCase)) return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("lastMergeSourceCommit").Str("commitId");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse("New commits since you looked — refresh.");
        var sha7 = sha.Length > 7 ? sha[..7] : sha;
        return new ActionPreview(true, $"Approve PR #{item.Number} in {item.Repo} at {sha7}", $"Approve PR #{item.Number}",
            ActionText.PrWarning(item, run), null, sha);
    }

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, string? comment, CancellationToken ct)
    {
        ActionResult result;
        try
        {
            result = action == ItemAction.ApprovePr ? await ExecutePrAsync(item, comment, ct) : await ExecuteApprovalAsync(item, action, comment, ct);
        }
        catch (HttpRequestException ex) { result = ActionText.FromHttpError(ex); }
        Log.Info($"action {action} {item.Key}: {(result.Ok ? "ok" : "failed")} (comment {comment?.Length ?? 0} chars)");
        return result;
    }

    async Task<ActionResult> ExecuteApprovalAsync(InboxItem item, ItemAction action, string? comment, CancellationToken ct)
    {
        var body = new[] { new { approvalId = item.ApprovalId, status = action == ItemAction.Approve ? "approved" : "rejected", comment = comment ?? "" } };
        await client.SendAsync(HttpMethod.Patch, $"{Base(item)}/_apis/pipelines/approvals?{ApprovalApi}", body, ct);
        return new ActionResult(true, null);
    }

    async Task<ActionResult> ExecutePrAsync(InboxItem item, string? comment, CancellationToken ct)
    {
        var ctx = await directory.GetOrgContextAsync(item.Org!, ct);
        var prUrl = $"{Base(item)}/_apis/git/repositories/{item.RepoId}/pullRequests/{item.Number}";
        await client.SendAsync(HttpMethod.Put, $"{prUrl}/reviewers/{ctx.MyId}?api-version=7.1", new { vote = 10 }, ct);
        if (string.IsNullOrWhiteSpace(comment)) return new ActionResult(true, null);
        try
        {
            await client.SendAsync(HttpMethod.Post, $"{prUrl}/threads?api-version=7.1",
                new { comments = new[] { new { parentCommentId = 0, content = comment, commentType = 1 } }, status = 1 }, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ActionResult(true, $"Approved, but the comment couldn't be posted: {ex.Message}");
        }
        return new ActionResult(true, null);
    }
}
