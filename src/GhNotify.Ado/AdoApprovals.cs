using GhNotify.Core;

namespace GhNotify.Ado;

public static class AdoApprovals
{
    public static async Task<IReadOnlyList<InboxItem>> FetchAsync(AdoClient c, string org, AdoProject p, AdoOrgContext ctx, CancellationToken ct)
    {
        var baseUrl = AdoPullRequests.ProjectUrl(org, p);
        var json = await c.GetAsync($"{baseUrl}/_apis/pipelines/approvals?state=pending&$expand=steps&api-version=7.1-preview.1", ct);
        var items = new List<InboxItem>();
        foreach (var a in json.Arr("value"))
        {
            if (!string.Equals(a.Str("status"), "pending", StringComparison.OrdinalIgnoreCase)) continue;
            var mine = a.Arr("steps").Any(s =>
                string.Equals(s.Str("status"), "pending", StringComparison.OrdinalIgnoreCase) &&
                s.Obj("assignedApprover").Str("id") is { } id && ctx.MyIdentityIds.Contains(id));
            if (!mine || a.Str("id") is not { } approvalId) continue;

            var pipeline = a.Obj("pipeline");
            var name = pipeline.Str("name") ?? "Pipeline";
            var run = pipeline.Obj("owner").Str("name");
            var url = pipeline.Obj("owner").Obj("_links").Obj("web").Str("href") ?? $"{baseUrl}/_build";
            var instructions = a.Str("instructions");
            items.Add(new InboxItem($"ado:approval:{org}/{p.Name}/{approvalId}", Provider.Ado, ItemKind.ApprovalPending,
                $"{org}/{p.Name}", run is null ? name : $"{name} {run}", url, "", a.Date("createdOn"), null, null,
                string.IsNullOrWhiteSpace(instructions) ? null : $"Instructions: {instructions}"));
        }
        return items;
    }
}
