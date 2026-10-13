using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubItemActions(GitHubClient client) : IItemActions
{
    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) =>
        item.Kind switch
        {
            ItemKind.ApprovalPending when item.RunId is null => [],
            ItemKind.ReviewRequested when item.Number is null => [],
            _ => ActionText.DefaultActions(item),
        };

    public async Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct)
    {
        try
        {
            return action == ItemAction.ApprovePr
                ? await PreviewPrAsync(item, run, ct)
                : await PreviewDeploymentAsync(item, action, ct);
        }
        catch (HttpRequestException ex)
        {
            var mapped = ActionText.FromHttpError(ex);
            return ActionText.Refuse(mapped.Error ?? ex.Message);
        }
        catch (RateLimitedException ex) { return ActionText.Refuse(ex.Message); }
        catch (SourceAuthException ex) { return ActionText.Refuse(ex.Message); }
    }

    async Task<ActionPreview> PreviewDeploymentAsync(InboxItem item, ItemAction action, CancellationToken ct)
    {
        if (item.RunId is not { } runId) return ActionText.Refuse("No longer waiting on you");
        var pending = await client.GetAsync($"repos/{item.Repo}/actions/runs/{runId}/pending_deployments", ct);
        var envs = pending.ValueKind == JsonValueKind.Array
            ? pending.EnumerateArray().Where(e => e.Bool("current_user_can_approve"))
                .Select(e => (Id: e.Obj("environment").Long("id"), Name: e.Obj("environment").Str("name")))
                .Where(e => e.Id is not null && e.Name is not null).ToList()
            : [];
        if (envs.Count == 0) return ActionText.Refuse("No longer waiting on you");
        var verb = action == ItemAction.Approve ? "Approve" : "Reject";
        var names = string.Join(", ", envs.Select(e => e.Name));
        return new ActionPreview(true, $"{verb} deployment of {item.Repo} run {runId} to {names}", $"{verb} {names}",
            null, null, envs.Select(e => e.Id!.Value).ToArray());
    }

    async Task<ActionPreview> PreviewPrAsync(InboxItem item, AgentRun? run, CancellationToken ct)
    {
        if (item.Number is not { } n) return ActionText.Refuse("No longer waiting on you");
        var pr = await client.GetAsync($"repos/{item.Repo}/pulls/{n}", ct);
        if (pr.Str("state") != "open") return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("head").Str("sha");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse("New commits since you looked — refresh.");
        return new ActionPreview(true, $"Approve PR #{n} in {item.Repo} at {sha[..Math.Min(7, sha.Length)]}", $"Approve PR #{n}",
            ActionText.PrWarning(item, run), null, sha);
    }

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, string? comment, CancellationToken ct)
    {
        ActionResult result;
        try
        {
            if (action == ItemAction.ApprovePr)
            {
                var sha = (string)preview.Token!;
                var path = $"repos/{item.Repo}/pulls/{item.Number}/reviews";
                if (string.IsNullOrWhiteSpace(comment))
                    await client.PostAsync(path, new { @event = "APPROVE", commit_id = sha }, ct);
                else
                    await client.PostAsync(path, new { @event = "APPROVE", commit_id = sha, body = comment }, ct);
            }
            else
            {
                await client.PostAsync($"repos/{item.Repo}/actions/runs/{item.RunId}/pending_deployments",
                    new { environment_ids = (long[])preview.Token!, state = action == ItemAction.Approve ? "approved" : "rejected", comment = comment ?? "" }, ct);
            }
            result = new ActionResult(true, null);
        }
        catch (HttpRequestException ex) { result = ActionText.FromHttpError(ex); }
        catch (RateLimitedException ex) { result = new ActionResult(false, ex.Message); }
        catch (SourceAuthException ex) { result = new ActionResult(false, ex.Message); }
        Log.Info($"action {action} {item.Key}: {(result.Ok ? "ok" : "failed")} (comment {comment?.Length ?? 0} chars)");
        return result;
    }
}
