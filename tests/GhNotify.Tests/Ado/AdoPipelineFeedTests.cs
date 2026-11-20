using System.Net;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoPipelineFeedTests
{
    static readonly DateTimeOffset Since = DateTimeOffset.Parse("2026-11-19T09:00:00Z");

    static PipelineSubscription Sub(Action<PipelineSubscription>? edit = null)
    {
        var s = new PipelineSubscription
        {
            Id = "s1", Provider = Provider.Ado, Org = "acme", Project = "Core Platform", PipelineId = 42, Pipeline = "Deploy",
            On = [PipelineOutcome.Failed], CreatedAt = Since,
        };
        edit?.Invoke(s);
        return s;
    }

    const string Builds = """
        { "value": [
          { "id": 1001, "buildNumber": "20261120.1", "status": "completed", "result": "failed", "sourceBranch": "refs/heads/main", "sourceVersion": "abc123",
            "finishTime": "2026-11-20T10:00:00Z", "queueTime": "2026-11-20T09:50:00Z", "definition": { "id": 42, "name": "Deploy" },
            "requestedFor": { "id": "me-id", "displayName": "Me" },
            "_links": { "web": { "href": "https://dev.azure.com/acme/Core%20Platform/_build/results?buildId=1001" } } },
          { "id": 1002, "buildNumber": "20261120.2", "status": "completed", "result": "partiallySucceeded", "sourceBranch": "refs/heads/release/1.2",
            "finishTime": "2026-11-20T11:00:00Z", "definition": { "id": 42, "name": "Deploy" }, "requestedFor": { "id": "someone-else", "displayName": "Bob" }, "_links": { } },
          { "id": 1003, "buildNumber": "20261120.3", "status": "completed", "result": "canceled", "sourceBranch": "refs/heads/main",
            "finishTime": "2026-11-20T11:30:00Z", "definition": { "id": 42, "name": "Deploy" }, "requestedFor": { "id": "me-id", "displayName": "Me" }, "_links": { } },
          { "id": 1004, "buildNumber": "20261120.4", "status": "inProgress", "sourceBranch": "refs/heads/main",
            "definition": { "id": 42, "name": "Deploy" }, "requestedFor": { "id": "me-id", "displayName": "Me" }, "_links": { } },
          { "id": 1005, "buildNumber": "20261120.5", "status": "notStarted", "sourceBranch": "refs/heads/main",
            "definition": { "id": 42, "name": "Deploy" }, "requestedFor": { "id": "me-id", "displayName": "Me" }, "_links": { } }
        ] }
        """;

    const string Timeline = """
        { "records": [
          { "type": "Stage", "name": "Build", "state": "completed", "result": "succeeded", "attempt": 1, "finishTime": "2026-11-20T09:55:00Z" },
          { "type": "Stage", "name": "PROD", "state": "completed", "result": "failed", "attempt": 2, "finishTime": "2026-11-20T10:00:00Z" },
          { "type": "Stage", "name": "Warn", "state": "completed", "result": "succeededWithIssues", "attempt": 1, "finishTime": "2026-11-20T10:01:00Z" },
          { "type": "Stage", "name": "Skipped", "state": "completed", "result": "skipped", "attempt": 1, "finishTime": "2026-11-20T10:01:00Z" },
          { "type": "Stage", "name": "Later", "state": "pending", "attempt": 1 },
          { "type": "Job", "name": "Deploy job", "state": "completed", "result": "failed" }
        ] }
        """;

    static (AdoPipelineFeed Feed, FakeHttpHandler Http) Make(string builds = Builds)
    {
        var http = new FakeHttpHandler()
            .On("_apis/build/builds?", builds)
            .On("/timeline", Timeline)
            .On("connectionData", Fixture.Read("ado-connection.json"))
            .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
            .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
            .On("_apis/build/definitions", """{ "value": [ { "id": 42, "name": "Deploy", "path": "\\" }, { "id": 7, "name": "CI" } ] }""")
            .On("_apis/projects", """{ "value": [ { "id": "p1", "name": "Core Platform" }, { "id": "p2", "name": "Other" } ] }""");
        var client = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        return (new AdoPipelineFeed(client, new AdoDirectory(client)), http);
    }

    [Fact]
    public async Task Builds_map_to_runs_with_outcomes_and_links()
    {
        var (feed, _) = Make();
        var runs = await feed.RecentRunsAsync(Sub(), Since, false, default);
        Assert.Equal(5, runs.Count);

        var failed = runs[0];
        Assert.Equal(Provider.Ado, failed.Provider);
        Assert.Equal("acme/Core Platform", failed.Scope);
        Assert.Equal(42, failed.PipelineId);
        Assert.Equal("Deploy", failed.Pipeline);
        Assert.Equal("1001", failed.RunId);
        Assert.Equal("20261120.1", failed.RunNumber);
        Assert.True(failed.Attempt > 1); // the completion minute: a stage retry completes the build again
        Assert.Equal("main", failed.Branch);
        Assert.Equal("Me", failed.RequestedBy);
        Assert.Equal("abc123", failed.CommitSha);
        Assert.Equal("https://dev.azure.com/acme/Core%20Platform/_build/results?buildId=1001", failed.Url);
        Assert.Equal(PipelineOutcome.Failed, failed.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), failed.Finished);

        Assert.Equal(PipelineOutcome.PartiallySucceeded, runs[1].Outcome);
        Assert.Equal("release/1.2", runs[1].Branch);
        Assert.Equal("https://dev.azure.com/acme/Core%20Platform/_build/results?buildId=1002", runs[1].Url); // no web link: built from the id
        Assert.Null(runs[2].Outcome); // canceled never alerts
        Assert.Null(runs[3].Outcome); // in progress
        Assert.Null(runs[3].Finished);
    }

    [Fact]
    public async Task The_query_asks_for_the_definition_the_window_and_finished_builds_only()
    {
        var (feed, http) = Make();
        await feed.RecentRunsAsync(Sub(), Since, includeInProgress: false, default);
        var url = Uri.UnescapeDataString(http.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("_apis/build/builds?")).RequestUri!.AbsoluteUri);
        Assert.Contains("https://dev.azure.com/acme/Core%20Platform/_apis/build/builds?", http.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("definitions=42", url);
        Assert.Contains("statusFilter=completed", url);
        Assert.Contains("queryOrder=finishTimeDescending", url);
        Assert.Contains("minTime=2026-11-19T09:00:00", url);
    }

    [Fact]
    public async Task In_progress_runs_are_requested_by_queue_time_without_a_status_filter()
    {
        var (feed, http) = Make();
        await feed.RecentRunsAsync(Sub(s => s.Stages = ["PROD"]), Since, includeInProgress: true, default);
        var url = Uri.UnescapeDataString(http.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("_apis/build/builds?")).RequestUri!.AbsoluteUri);
        Assert.Contains("queryOrder=queueTimeDescending", url);
        Assert.DoesNotContain("statusFilter", url);
    }

    [Fact]
    public async Task Timelines_are_read_only_when_stages_are_subscribed_and_map_stage_results()
    {
        var (plain, plainHttp) = Make();
        await plain.RecentRunsAsync(Sub(), Since, false, default);
        Assert.DoesNotContain(plainHttp.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/timeline"));

        var (feed, http) = Make();
        var runs = await feed.RecentRunsAsync(Sub(s => s.Stages = ["*"]), Since, true, default);
        var stages = runs[0].Stages;
        Assert.Equal(["Build", "PROD", "Warn", "Skipped", "Later"], stages.Select(s => s.Name));
        Assert.Equal([PipelineOutcome.Succeeded, PipelineOutcome.Failed, PipelineOutcome.PartiallySucceeded, null, null], stages.Select(s => s.Outcome));
        Assert.Equal(2, stages[1].Attempt);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), stages[1].Finished);
        Assert.Contains(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/builds/1001/timeline"));
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/builds/1005/timeline")); // not started: nothing to read
    }

    [Fact]
    public async Task A_completed_builds_timeline_is_fetched_once_but_a_running_one_every_poll()
    {
        var (feed, http) = Make();
        var sub = Sub(s => s.Stages = ["*"]);
        await feed.RecentRunsAsync(sub, Since, true, default);
        await feed.RecentRunsAsync(sub, Since, true, default);
        int Count(string id) => http.Requests.Count(r => r.RequestUri!.AbsoluteUri.Contains($"/builds/{id}/timeline"));
        Assert.Equal(1, Count("1001"));
        Assert.Equal(2, Count("1004"));
    }

    [Fact]
    public async Task Only_mine_marks_runs_by_my_identity_and_skips_timelines_for_other_peoples_builds()
    {
        var (feed, http) = Make();
        var runs = await feed.RecentRunsAsync(Sub(s => { s.OnlyMine = true; s.Stages = ["*"]; }), Since, true, default);
        Assert.True(runs[0].RequestedByMe);
        Assert.False(runs[1].RequestedByMe);
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/builds/1002/timeline"));
    }

    [Fact]
    public async Task Without_only_mine_the_identity_is_not_looked_up()
    {
        var (feed, http) = Make();
        var runs = await feed.RecentRunsAsync(Sub(), Since, false, default);
        Assert.All(runs, r => Assert.Null(r.RequestedByMe));
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("connectionData"));
    }

    [Fact]
    public async Task A_missing_pipeline_surfaces_as_an_http_error_for_that_subscription()
    {
        var http = new FakeHttpHandler().On("_apis/build/builds?", "{}", HttpStatusCode.NotFound);
        var client = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new AdoPipelineFeed(client, new AdoDirectory(client)).RecentRunsAsync(Sub(), Since, false, default));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Pickers_list_projects_and_pipelines()
    {
        var (feed, _) = Make();
        Assert.Equal(["Core Platform", "Other"], await feed.ListProjectsAsync("acme", default));
        var pipelines = await feed.ListPipelinesAsync("acme", "Core Platform", default);
        Assert.Equal([new PipelinePick(42, "Deploy"), new PipelinePick(7, "CI")], pipelines);
    }

    static string OneBuild(string finish) => $$"""
        { "value": [ { "id": 1001, "buildNumber": "1", "status": "completed", "result": "failed", "sourceBranch": "refs/heads/main",
          "finishTime": "{{finish}}", "definition": { "id": 42, "name": "Deploy" }, "requestedFor": { "id": "me-id" }, "_links": { } } ] }
        """;

    static string OneStage(string result, int attempt) => $$"""
        { "records": [ { "type": "Stage", "name": "PROD", "state": "completed", "result": "{{result}}", "attempt": {{attempt}}, "finishTime": "2026-11-20T10:20:00Z" } ] }
        """;

    [Fact]
    public async Task A_stage_retry_that_completes_the_same_build_again_is_read_afresh_not_from_the_cache()
    {
        string builds = OneBuild("2026-11-20T10:00:00Z"), timeline = OneStage("failed", 1);
        var http = new FakeHttpHandler()
            .On(r => r.RequestUri!.AbsoluteUri.Contains("_apis/build/builds?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, builds))
            .On(r => r.RequestUri!.AbsoluteUri.Contains("/timeline"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, timeline));
        var client = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        var feed = new AdoPipelineFeed(client, new AdoDirectory(client));
        var sub = Sub(s => s.Stages = ["PROD"]);

        var first = (await feed.RecentRunsAsync(sub, Since, true, default)).Single();
        Assert.Equal(PipelineOutcome.Failed, first.Stages.Single().Outcome);

        builds = OneBuild("2026-11-20T10:20:00Z");
        timeline = OneStage("succeeded", 2);
        var second = (await feed.RecentRunsAsync(sub, Since, true, default)).Single();
        Assert.Equal(PipelineOutcome.Succeeded, second.Stages.Single().Outcome);
        Assert.Equal(2, second.Stages.Single().Attempt);
    }

    [Fact]
    public async Task A_whole_run_that_completes_again_gets_a_new_key_so_a_retry_is_a_new_row()
    {
        string builds = OneBuild("2026-11-20T10:00:00Z");
        var http = new FakeHttpHandler().On(r => r.RequestUri!.AbsoluteUri.Contains("_apis/build/builds?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, builds));
        var client = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        var feed = new AdoPipelineFeed(client, new AdoDirectory(client));
        var first = (await feed.RecentRunsAsync(Sub(), Since, false, default)).Single();
        builds = OneBuild("2026-11-20T10:20:00Z");
        var second = (await feed.RecentRunsAsync(Sub(), Since, false, default)).Single();
        Assert.NotEqual(PipelineMatcher.Key(first, null), PipelineMatcher.Key(second, null));
        Assert.Equal(PipelineMatcher.Key(first, null), PipelineMatcher.Key(first, null));
    }
}
