namespace GhNotify.Core;

/// <summary>Pure matching of a pipeline run against one subscription, plus the item and key it produces.</summary>
public static class PipelineMatcher
{
    public static IReadOnlyList<PipelineHit> Match(PipelineSubscription sub, PipelineRunInfo run)
    {
        if (sub.Branches.Count > 0 && (run.Branch is null || !sub.Branches.Any(b => ItemMatcher.Glob(b, run.Branch)))) return [];
        if (sub.OnlyMine && run.RequestedByMe != true) return [];

        var hits = new List<PipelineHit>();
        void Consider(string? stage, PipelineOutcome? outcome, DateTimeOffset? finished)
        {
            if (outcome is { } o && finished is { } f && f >= sub.CreatedAt && sub.On.Contains(o)) hits.Add(new PipelineHit(stage, o, f));
        }

        if (sub.Stages.Count == 0) Consider(null, run.Outcome, run.Finished);
        else
            foreach (var s in run.Stages.Where(s => sub.Stages.Any(g => ItemMatcher.Glob(g, s.Name))))
                Consider(s.Name, s.Outcome, s.Finished);
        return hits;
    }

    /// <summary>A retried ADO stage carries its own attempt, so its new result is a new row while the other stages keep their keys.</summary>
    public static string Key(PipelineRunInfo run, string? stage)
    {
        var attempt = stage is not null && run.Stages.FirstOrDefault(s => s.Name == stage) is { Attempt: > 0 } st ? st.Attempt : run.Attempt;
        return $"pipe:{(run.Provider == Provider.Ado ? "ado" : "gh")}:{run.Scope}:{run.PipelineId}:{run.RunId}:{attempt}" + (stage is null ? "" : ":" + stage);
    }

    public static InboxItem ToItem(PipelineSubscription sub, PipelineRunInfo run, PipelineHit hit) =>
        new(Key(run, hit.Stage), run.Provider, ItemKind.PipelineRun, run.Scope,
            hit.Stage is null ? run.Pipeline : $"{run.Pipeline} · {hit.Stage}", run.Url, run.RequestedBy ?? "", hit.Finished,
            run.CommitSha, null, hit.Outcome.ToString())
        {
            SubscriptionId = sub.Id,
            Finished = hit.Finished,
            Details = DetailList.Of(("Pipeline", run.Pipeline), ("Run", run.RunNumber), ("Attempt", run.Provider == Provider.Ado ? null : run.Attempt.ToString()), ("Stage", hit.Stage),
                ("Branch", run.Branch), ("Triggered by", run.RequestedBy), ("Result", hit.Outcome.ToString()), ("Finished", DetailList.Local(hit.Finished))),
        };
}
