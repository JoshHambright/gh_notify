using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubItemActions(GitHubClient client) : IItemActions
{
    sealed record DeployToken(string Repo, long RunId, ItemAction Action, long[] EnvIds);
    sealed record PrToken(string Repo, int Number, string Sha);

    const string Mismatch = "This action no longer matches what was shown — start again";
    const string StaleChanged = "This changed since you looked — refreshing";
    const string StaleHead = "New commits since you looked — refresh.";

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
        catch (HttpRequestException ex) { var m = ActionText.FromHttpError(ex); return ActionText.Refuse(m.Error ?? ex.Message); }
        catch (RateLimitedException ex) { return ActionText.Refuse(ex.Message); }
        catch (SourceAuthException ex) { return ActionText.Refuse(ex.Message); }
    }

    async Task<List<(long Id, string Name)>> ApprovableAsync(string repo, long runId, CancellationToken ct)
    {
        var pending = await client.GetAsync($"repos/{repo}/actions/runs/{runId}/pending_deployments", ct);
        return pending.ValueKind == JsonValueKind.Array
            ? pending.EnumerateArray().Where(e => e.Bool("current_user_can_approve"))
                .Select(e => (Id: e.Obj("environment").Long("id"), Name: e.Obj("environment").Str("name")))
                .Where(e => e.Id is not null && e.Name is not null).Select(e => (e.Id!.Value, e.Name!)).ToList()
            : [];
    }

    async Task<ActionPreview> PreviewDeploymentAsync(InboxItem item, ItemAction action, CancellationToken ct)
    {
        if (item.RunId is not { } runId) return ActionText.Refuse("No longer waiting on you");
        var envs = await ApprovableAsync(item.Repo, runId, ct);
        if (envs.Count == 0) return ActionText.Refuse("No longer waiting on you");
        var verb = action == ItemAction.Approve ? "Approve" : "Reject";
        var names = string.Join(", ", envs.Select(e => e.Name));
        return new ActionPreview(true, $"{verb} deployment of {item.Repo} run {runId} to {names}", $"{verb} {names}",
            null, null, new DeployToken(item.Repo, runId, action, envs.Select(e => e.Id).ToArray()));
    }

    async Task<ActionPreview> PreviewPrAsync(InboxItem item, AgentRun? run, CancellationToken ct)
    {
        if (item.Number is not { } n) return ActionText.Refuse("No longer waiting on you");
        var pr = await client.GetAsync($"repos/{item.Repo}/pulls/{n}", ct);
        if (pr.Str("state") != "open") return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("head").Str("sha");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse(StaleHead);
        return new ActionPreview(true, $"Approve PR #{n} in {item.Repo} at {sha[..Math.Min(7, sha.Length)]}", $"Approve PR #{n}",
            ActionText.PrWarning(item, run), null, new PrToken(item.Repo, n, sha));
    }

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        var comment = input.Comment;
        var result = new ActionResult(false, "failed");
        try
        {
            result = await ExecuteCoreAsync(item, action, preview, input, ct);
        }
        catch (HttpRequestException ex) { result = ActionText.FromHttpError(ex); }
        catch (RateLimitedException ex) { result = new ActionResult(false, ex.Message); }
        catch (SourceAuthException ex) { result = new ActionResult(false, ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = new ActionResult(false, "The request timed out — it may have been sent. Refresh to check.", Stale: true);
        }
        catch (JsonException)
        {
            result = new ActionResult(false, "Unexpected response from GitHub — it may have been sent. Refresh to check.", Stale: true);
        }
        finally
        {
            Log.Info($"action {action} {item.Key}: {(result.Ok ? "ok" : "failed")} (comment {comment?.Length ?? 0} chars)");
        }
        return result;
    }

    async Task<ActionResult> ExecuteCoreAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        var comment = input.Comment;
        var mismatch = new ActionResult(false, Mismatch);
        if (!preview.Allowed) return mismatch;

        if (action == ItemAction.ApprovePr)
        {
            if (preview.Token is not PrToken t || t.Repo != item.Repo || item.Number != t.Number) return mismatch;
            var pr = await client.GetAsync($"repos/{t.Repo}/pulls/{t.Number}", ct);
            if (pr.Str("state") != "open" || pr.Obj("head").Str("sha") != t.Sha)
                return new ActionResult(false, StaleHead, Stale: true);
            var path = $"repos/{t.Repo}/pulls/{t.Number}/reviews";
            if (string.IsNullOrWhiteSpace(comment))
                await client.PostAsync(path, new { @event = "APPROVE", commit_id = t.Sha }, ct);
            else
                await client.PostAsync(path, new { @event = "APPROVE", commit_id = t.Sha, body = comment }, ct);
            return new ActionResult(true, null);
        }

        if (preview.Token is not DeployToken d || d.Action != action || d.Repo != item.Repo || item.RunId != d.RunId) return mismatch;
        var live = (await ApprovableAsync(d.Repo, d.RunId, ct)).Select(e => e.Id).ToHashSet();
        if (!d.EnvIds.All(live.Contains)) return new ActionResult(false, StaleChanged, Stale: true);
        await client.PostAsync($"repos/{d.Repo}/actions/runs/{d.RunId}/pending_deployments",
            new { environment_ids = d.EnvIds, state = action == ItemAction.Approve ? "approved" : "rejected", comment = comment ?? "" }, ct);
        return new ActionResult(true, null);
    }
}
