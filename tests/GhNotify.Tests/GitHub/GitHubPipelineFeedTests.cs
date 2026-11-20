using System.Net;
using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.GitHub;

public class GitHubPipelineFeedTests
{
    static readonly DateTimeOffset Since = DateTimeOffset.Parse("2026-11-19T09:00:00Z");

    static PipelineSubscription Sub(Action<PipelineSubscription>? edit = null)
    {
        var s = new PipelineSubscription
        {
            Id = "s1", Provider = Provider.GitHub, Repo = "acme/shop", PipelineId = 555, Pipeline = "Deploy",
            On = [PipelineOutcome.Failed], CreatedAt = Since,
        };
        edit?.Invoke(s);
        return s;
    }

    const string Runs = """
        { "workflow_runs": [
          { "id": 9001, "run_number": 14, "run_attempt": 2, "status": "completed", "conclusion": "failure", "head_branch": "main", "head_sha": "abc123",
            "html_url": "https://github.com/acme/shop/actions/runs/9001", "updated_at": "2026-11-20T10:00:00Z", "name": "Deploy",
            "triggering_actor": { "login": "Me" }, "actor": { "login": "bot" } },
          { "id": 9002, "run_number": 15, "run_attempt": 1, "status": "completed", "conclusion": "success", "head_branch": "release/1.2", "head_sha": "def456",
            "html_url": "https://github.com/acme/shop/actions/runs/9002", "updated_at": "2026-11-20T11:00:00Z", "name": "Deploy",
            "actor": { "login": "bob" } },
          { "id": 9003, "run_number": 16, "run_attempt": 1, "status": "completed", "conclusion": "cancelled", "head_branch": "main", "updated_at": "2026-11-20T11:30:00Z",
            "html_url": "https://github.com/acme/shop/actions/runs/9003", "actor": { "login": "me" } },
          { "id": 9004, "run_number": 17, "run_attempt": 1, "status": "completed", "conclusion": "timed_out", "head_branch": "main", "updated_at": "2026-11-20T11:40:00Z",
            "html_url": "https://github.com/acme/shop/actions/runs/9004", "actor": { "login": "me" } },
          { "id": 9005, "run_number": 18, "run_attempt": 1, "status": "in_progress", "head_branch": "main", "updated_at": "2026-11-20T11:50:00Z",
            "html_url": "https://github.com/acme/shop/actions/runs/9005", "actor": { "login": "me" } },
          { "id": 9006, "run_number": 19, "run_attempt": 1, "status": "queued", "head_branch": "main", "updated_at": "2026-11-20T11:55:00Z",
            "html_url": "https://github.com/acme/shop/actions/runs/9006", "actor": { "login": "me" } }
        ] }
        """;

    const string Jobs = """
        { "jobs": [
          { "name": "build", "status": "completed", "conclusion": "success", "run_attempt": 1, "completed_at": "2026-11-20T09:55:00Z" },
          { "name": "deploy-prod", "status": "completed", "conclusion": "failure", "run_attempt": 2, "completed_at": "2026-11-20T10:00:00Z" },
          { "name": "skipped", "status": "completed", "conclusion": "skipped", "run_attempt": 1, "completed_at": "2026-11-20T10:00:00Z" },
          { "name": "later", "status": "queued", "run_attempt": 1 }
        ] }
        """;

    static (GitHubPipelineFeed Feed, FakeHttpHandler Http) Make(string runs = Runs)
    {
        var http = new FakeHttpHandler()
            .On("/actions/workflows/555/runs", runs)
            .On("/actions/runs/", Jobs)
            .On("api.github.com/user", """{ "login": "me" }""")
            .On("/actions/workflows?", """{ "workflows": [ { "id": 555, "name": "Deploy", "state": "active" }, { "id": 556, "name": "Old", "state": "disabled_manually" } ] }""");
        return (new GitHubPipelineFeed(new GitHubClient(new HttpClient(http), new FakeTokenProvider())), http);
    }

    [Fact]
    public async Task Workflow_runs_map_to_runs_with_outcomes_and_attempts()
    {
        var (feed, _) = Make();
        var runs = await feed.RecentRunsAsync(Sub(), Since, false, default);
        Assert.Equal(6, runs.Count);

        var failed = runs[0];
        Assert.Equal(Provider.GitHub, failed.Provider);
        Assert.Equal("acme/shop", failed.Scope);
        Assert.Equal(555, failed.PipelineId);
        Assert.Equal("Deploy", failed.Pipeline);
        Assert.Equal("9001", failed.RunId);
        Assert.Equal("14", failed.RunNumber);
        Assert.Equal(2, failed.Attempt);
        Assert.Equal("main", failed.Branch);
        Assert.Equal("Me", failed.RequestedBy); // the triggering actor, not the workflow's actor
        Assert.Equal("abc123", failed.CommitSha);
        Assert.Equal("https://github.com/acme/shop/actions/runs/9001", failed.Url);
        Assert.Equal(PipelineOutcome.Failed, failed.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), failed.Finished);

        Assert.Equal(PipelineOutcome.Succeeded, runs[1].Outcome);
        Assert.Equal("bob", runs[1].RequestedBy); // falls back to actor
        Assert.Null(runs[2].Outcome); // cancelled never alerts
        Assert.Equal(PipelineOutcome.Failed, runs[3].Outcome); // timed_out is a failure
        Assert.Null(runs[4].Outcome);
        Assert.Null(runs[4].Finished);
        Assert.Null(runs[5].Outcome);
        Assert.All(runs, r => Assert.NotEqual(PipelineOutcome.PartiallySucceeded, r.Outcome));
    }

    [Fact]
    public async Task The_query_names_the_workflow_the_window_and_finished_runs_only()
    {
        var (feed, http) = Make();
        await feed.RecentRunsAsync(Sub(), Since, includeInProgress: false, default);
        var url = Uri.UnescapeDataString(http.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("/actions/workflows/555/runs")).RequestUri!.AbsoluteUri);
        Assert.Contains("repos/acme/shop/actions/workflows/555/runs", url);
        Assert.Contains("status=completed", url);
        Assert.Contains("created=>=2026-11-19T09:00:00Z", url);
    }

    [Fact]
    public async Task In_progress_runs_are_requested_without_a_status_filter()
    {
        var (feed, http) = Make();
        await feed.RecentRunsAsync(Sub(s => s.Stages = ["deploy*"]), Since, includeInProgress: true, default);
        var url = http.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("/actions/workflows/555/runs")).RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("status=", url);
    }

    [Fact]
    public async Task Jobs_are_read_only_for_stage_subscriptions_and_map_to_stages()
    {
        var (plain, plainHttp) = Make();
        await plain.RecentRunsAsync(Sub(), Since, false, default);
        Assert.DoesNotContain(plainHttp.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/jobs"));

        var (feed, http) = Make();
        var runs = await feed.RecentRunsAsync(Sub(s => s.Stages = ["*"]), Since, true, default);
        var stages = runs[0].Stages;
        Assert.Equal(["build", "deploy-prod", "skipped", "later"], stages.Select(s => s.Name));
        Assert.Equal([PipelineOutcome.Succeeded, PipelineOutcome.Failed, null, null], stages.Select(s => s.Outcome));
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), stages[1].Finished);
        Assert.Equal(2, stages[1].Attempt);
        Assert.Contains(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/actions/runs/9001/jobs"));
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/actions/runs/9006/jobs")); // queued: nothing to read
    }

    [Fact]
    public async Task A_completed_runs_jobs_are_fetched_once_but_a_running_ones_every_poll()
    {
        var (feed, http) = Make();
        var sub = Sub(s => s.Stages = ["*"]);
        await feed.RecentRunsAsync(sub, Since, true, default);
        await feed.RecentRunsAsync(sub, Since, true, default);
        int Count(string id) => http.Requests.Count(r => r.RequestUri!.AbsoluteUri.Contains($"/actions/runs/{id}/jobs"));
        Assert.Equal(1, Count("9001"));
        Assert.Equal(2, Count("9005"));
    }

    [Fact]
    public async Task Only_mine_compares_the_triggering_actor_to_the_signed_in_user_ignoring_case()
    {
        var (feed, http) = Make();
        var runs = await feed.RecentRunsAsync(Sub(s => s.OnlyMine = true), Since, false, default);
        Assert.True(runs[0].RequestedByMe); // "Me" vs "me"
        Assert.False(runs[1].RequestedByMe);
        Assert.Single(http.Requests, r => r.RequestUri!.AbsoluteUri.EndsWith("/user"));
    }

    [Fact]
    public async Task When_the_signed_in_user_cannot_be_read_only_mine_stays_unknown()
    {
        var http = new FakeHttpHandler().On("/actions/workflows/555/runs", Runs).On("/user", "{}", HttpStatusCode.Forbidden);
        var feed = new GitHubPipelineFeed(new GitHubClient(new HttpClient(http), new FakeTokenProvider()));
        var runs = await feed.RecentRunsAsync(Sub(s => s.OnlyMine = true), Since, false, default);
        Assert.All(runs, r => Assert.Null(r.RequestedByMe));
    }

    [Fact]
    public async Task Without_only_mine_the_user_is_not_looked_up()
    {
        var (feed, http) = Make();
        await feed.RecentRunsAsync(Sub(), Since, false, default);
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.EndsWith("/user"));
    }

    [Fact]
    public async Task A_missing_workflow_surfaces_as_an_http_error_for_that_subscription()
    {
        var http = new FakeHttpHandler().On("/actions/workflows/555/runs", """{"message":"Not Found"}""", HttpStatusCode.NotFound);
        var feed = new GitHubPipelineFeed(new GitHubClient(new HttpClient(http), new FakeTokenProvider()));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => feed.RecentRunsAsync(Sub(), Since, false, default));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task The_picker_lists_active_workflows()
    {
        var (feed, _) = Make();
        Assert.Equal([new PipelinePick(555, "Deploy")], await feed.ListWorkflowsAsync("acme/shop", default));
    }
}
