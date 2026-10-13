using System.Text.Json;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Core;

public class HookDispatcherTests
{
    static InboxItem Item(ItemKind kind = ItemKind.ApprovalPending) =>
        new("gh:approval:acme/deploy/runs/1", Provider.GitHub, kind, "acme/deploy", "Release", "https://u", "carol",
            DateTimeOffset.UnixEpoch, null, null, "Environment: production");

    static AppConfig Cfg(params HookConfig[] hooks) => new() { Hooks = [.. hooks] };

    [Fact]
    public async Task Runs_matching_hooks_with_stdin_json_and_env()
    {
        var runner = new FakeProcessRunner();
        var cfg = Cfg(
            new HookConfig { Event = HookEvent.ItemAdded, Match = new ItemMatch { Kind = ItemKind.ApprovalPending }, Run = ["pwsh", "-File", @"C:\s\ping.ps1"] },
            new HookConfig { Event = HookEvent.ItemAdded, Match = new ItemMatch { Kind = ItemKind.ReviewRequested }, Run = ["never"] },
            new HookConfig { Event = HookEvent.ItemRemoved, Run = ["never-either"] });

        await new HookDispatcher(runner, () => cfg).DispatchAsync(HookEvent.ItemAdded, Item());

        var call = Assert.Single(runner.Calls);
        Assert.Equal("pwsh", call.FileName);
        Assert.Equal(["-File", @"C:\s\ping.ps1"], call.Args);
        Assert.Equal(TimeSpan.FromSeconds(60), call.Timeout);
        Assert.Equal("ItemAdded", call.Env!["GHN_EVENT"]);
        Assert.Equal("gh:approval:acme/deploy/runs/1", call.Env["GHN_KEY"]);
        Assert.Equal("https://u", call.Env["GHN_URL"]);
        Assert.Equal("ApprovalPending", call.Env["GHN_KIND"]);
        Assert.Equal("acme/deploy", call.Env["GHN_REPO"]);
        using var doc = JsonDocument.Parse(call.StdIn!);
        Assert.Equal("ItemAdded", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("Release", doc.RootElement.GetProperty("item").GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("agent").ValueKind);
    }

    [Fact]
    public async Task Agent_finished_includes_run()
    {
        var runner = new FakeProcessRunner();
        var cfg = Cfg(new HookConfig { Event = HookEvent.AgentFinished, Run = ["log"] });
        var run = new AgentRun { Key = "k", Profile = "peer-review", State = AgentState.Ready, Summary = "fine" };
        await new HookDispatcher(runner, () => cfg).DispatchAsync(HookEvent.AgentFinished, Item(), run);
        using var doc = JsonDocument.Parse(runner.Calls.Single().StdIn!);
        Assert.Equal("fine", doc.RootElement.GetProperty("agent").GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Failures_never_throw()
    {
        var cfg = Cfg(new HookConfig { Event = HookEvent.ItemAdded, Run = ["x"] }, new HookConfig { Event = HookEvent.ItemAdded, Run = ["y"] });
        var throwing = new FakeProcessRunner { Handler = s => s.FileName == "x" ? throw new FileNotFoundException("x") : FakeProcessRunner.Fail("bad", 3) };
        await new HookDispatcher(throwing, () => cfg).DispatchAsync(HookEvent.ItemAdded, Item());
        Assert.Equal(2, throwing.Calls.Count);
    }

    [Fact]
    public async Task Config_failure_never_throws()
    {
        var runner = new FakeProcessRunner();
        var dispatcher = new HookDispatcher(runner, () => throw new InvalidOperationException("boom"));
        await dispatcher.DispatchAsync(HookEvent.ItemAdded, Item());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task No_hooks_means_no_processes()
    {
        var runner = new FakeProcessRunner();
        await new HookDispatcher(runner, () => new AppConfig()).DispatchAsync(HookEvent.ItemAdded, Item());
        Assert.Empty(runner.Calls);
    }
}
