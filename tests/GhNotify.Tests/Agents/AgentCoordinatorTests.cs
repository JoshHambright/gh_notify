using GhNotify.Agents;
using GhNotify.Core;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Agents;

public class AgentCoordinatorTests
{
    const string Sid = "11111111-1111-1111-1111-111111111111";

    sealed class Repos(string? clone) : ILocalRepoIndex { public string? FindClone(string url) => clone; }

    sealed class Skills(bool installed) : ISkillLocator { public bool IsInstalled(string s, string? r) => installed; }

    sealed class Worktrees : IWorktreeManager
    {
        public List<(string Clone, string Ref, string Tag)> Created { get; } = [];
        public List<string> Removed { get; } = [];
        public Exception? Throw { get; set; }
        public Task<string> CreateAsync(string clone, string fetchRef, string tag, CancellationToken ct)
        {
            if (Throw is not null) throw Throw;
            lock (Created) Created.Add((clone, fetchRef, tag));
            return Task.FromResult(Path.Combine(@"C:\wt", tag));
        }
        public Func<Task>? OnRemove { get; set; }
        public async Task RemoveAsync(string clone, string wt, string tag, CancellationToken ct)
        {
            Removed.Add(wt);
            if (OnRemove is not null) await OnRemove();
        }
    }

    sealed class Claude : IClaudeRunner
    {
        public List<(string Cwd, string Prompt)> Calls { get; } = [];
        public Func<Task<AgentOutcome>> Next { get; set; } = () => Task.FromResult(new AgentOutcome(true, Sid, "looks fine", 0.1m, null));
        public Task<AgentOutcome> RunAsync(string cwd, string prompt, AgentProfile p, IReadOnlyList<string> allowed, TimeSpan t, CancellationToken ct)
        {
            lock (Calls) Calls.Add((cwd, prompt));
            return Next();
        }
    }

    sealed class FakeCommands : ICommandRunner
    {
        public List<(string Cwd, IReadOnlyList<string> Command)> Calls { get; } = [];
        public Task<AgentOutcome> RunAsync(string cwd, IReadOnlyList<string> command, InboxItem item, TimeSpan t, CancellationToken ct)
        {
            lock (Calls) Calls.Add((cwd, command));
            return Task.FromResult(new AgentOutcome(true, null, "done", null, null) { ResultText = "out" });
        }
    }

    static (AgentCoordinator, FakeCommands, Claude, string Scratch) MakeCmd(string? clone, bool exists = true)
    {
        var cmds = new FakeCommands();
        var claude = new Claude();
        var scratch = Path.Combine(Path.GetTempPath(), "ghn-scratch-" + Guid.NewGuid());
        var cfg = new AppConfig();
        var c = new AgentCoordinator(new AgentDeps(new Repos(clone), new Worktrees(), claude, new Skills(true), () => false, cmds, _ => exists),
            () => cfg, scratch);
        return (c, cmds, claude, scratch);
    }

    static AgentDecision CmdDecision() =>
        new(AgentMode.Button, "triage", new AgentProfile { Command = ["triage", "{url}"], NeedsWorktree = false });

    static InboxItem Item(string key = "gh:review:acme/api#12", string sha = "s1") =>
        new(key, Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "T", "https://github.com/acme/api/pull/12", "alice",
            DateTimeOffset.UnixEpoch, sha, 10, null) { Number = 12, SourceRef = "pull/12/head", CloneUrl = "https://github.com/acme/api" };

    static AgentDecision Decision(bool needsWorktree = true) =>
        new(AgentMode.Button, "peer-review", new AgentProfile { Prompt = "/peer-review {url}", RequiresSkill = "peer-review", NeedsWorktree = needsWorktree });

    static (AgentCoordinator, Worktrees, Claude, FakeTimeProvider) Make(
        string? clone = @"C:\dev\api", bool skill = true, bool claudeOk = true, int max = 2, IEnumerable<AgentRun>? restored = null)
    {
        var wts = new Worktrees();
        var claude = new Claude();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var cfg = new AppConfig { MaxConcurrentAgents = max };
        var c = new AgentCoordinator(new AgentDeps(new Repos(clone), wts, claude, new Skills(skill), () => claudeOk, new FakeCommands()),
            () => cfg, Path.Combine(Path.GetTempPath(), "ghn-scratch-" + Guid.NewGuid()), time, restored);
        return (c, wts, claude, time);
    }

    static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 200 && !cond(); i++) await Task.Delay(10);
        Assert.True(cond());
    }

    [Theory]
    [InlineData(null, true, true, "No local clone under repoRoots")]
    [InlineData(@"C:\dev\api", false, true, "Skill /peer-review not installed")]
    [InlineData(@"C:\dev\api", true, false, "claude CLI not found on PATH")]
    public void Explains_why_it_cannot_run(string? clone, bool skill, bool claudeOk, string reason)
    {
        var (c, _, _, _) = Make(clone, skill, claudeOk);
        Assert.Equal(reason, c.WhyCannotRun(Item(), Decision()));
        Assert.False(c.TryStart(Item(), Decision(), manual: true));
        Assert.Null(c.Get(Item().Key));
    }

    [Fact]
    public async Task Runs_in_worktree_and_records_outcome()
    {
        var (c, wts, claude, _) = Make();
        var seen = new List<AgentState>();
        c.RunChanged += r => { lock (seen) seen.Add(r.State); };

        Assert.True(c.TryStart(Item(), Decision(), manual: true));
        await c.WhenIdle();

        var run = c.Get(Item().Key)!;
        Assert.Equal(AgentState.Ready, run.State);
        Assert.Equal(Sid, run.SessionId);
        Assert.Equal("looks fine", run.Summary);
        Assert.Equal(0.1m, run.CostUsd);
        Assert.Equal(@"C:\dev\api", run.ClonePath);
        Assert.Equal(Path.Combine(@"C:\wt", ItemTag.For(Item().Key)), run.WorktreePath);
        Assert.Equal((@"C:\dev\api", "pull/12/head", ItemTag.For(Item().Key)), wts.Created.Single());
        Assert.Equal("/peer-review https://github.com/acme/api/pull/12", claude.Calls.Single().Prompt);
        Assert.Equal([AgentState.Queued, AgentState.Running, AgentState.Running, AgentState.Ready], seen);
    }

    [Fact]
    public async Task Profile_without_worktree_runs_in_scratch_and_needs_no_clone()
    {
        var (c, wts, claude, _) = Make(clone: null);
        Assert.Null(c.WhyCannotRun(Item(), Decision(needsWorktree: false)));
        Assert.True(c.TryStart(Item(), Decision(needsWorktree: false), manual: true));
        await c.WhenIdle();
        Assert.Empty(wts.Created);
        Assert.EndsWith(ItemTag.For(Item().Key), claude.Calls.Single().Cwd);
        Assert.True(Directory.Exists(claude.Calls.Single().Cwd));
    }

    [Fact]
    public async Task Respects_concurrency_limit()
    {
        var (c, _, claude, _) = Make(max: 1);
        var gate = new TaskCompletionSource<AgentOutcome>();
        claude.Next = () => gate.Task;
        c.TryStart(Item("a"), Decision(), true);
        c.TryStart(Item("b"), Decision(), true);
        await WaitFor(() => c.Runs.Values.Count(r => r.State == AgentState.Running) == 1);
        Assert.Single(c.Runs.Values, r => r.State == AgentState.Queued);
        gate.SetResult(new AgentOutcome(true, Sid, "ok", null, null));
        await c.WhenIdle();
        await WaitFor(() => c.Runs.Values.All(r => r.State == AgentState.Ready));
    }

    [Fact]
    public async Task Auto_never_reruns_but_manual_can()
    {
        var (c, _, claude, _) = Make();
        c.TryStart(Item(), Decision(), manual: false);
        await c.WhenIdle();
        Assert.False(c.TryStart(Item(), Decision(), manual: false));
        Assert.True(c.TryStart(Item(), Decision(), manual: true));
        await c.WhenIdle();
        Assert.Equal(2, claude.Calls.Count);
    }

    [Fact]
    public async Task Head_change_marks_outdated_including_while_running()
    {
        var (c, _, claude, _) = Make();
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        c.OnItemChanged(new ItemChange(Item(), Item(sha: "s2")));
        Assert.Equal(AgentState.Outdated, c.Get(Item().Key)!.State);

        var gate = new TaskCompletionSource<AgentOutcome>();
        claude.Next = () => gate.Task;
        c.TryStart(Item(sha: "s2"), Decision(), true);
        await WaitFor(() => c.Get(Item().Key)!.State == AgentState.Running);
        c.OnItemChanged(new ItemChange(Item(sha: "s2"), Item(sha: "s3")));
        gate.SetResult(new AgentOutcome(true, Sid, "ok", null, null));
        await c.WhenIdle();
        Assert.Equal(AgentState.Outdated, c.Get(Item().Key)!.State);
    }

    [Fact]
    public async Task Worktree_failure_is_a_failed_run()
    {
        var (c, wts, _, _) = Make();
        wts.Throw = new GitException("git fetch failed: no such ref");
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        var run = c.Get(Item().Key)!;
        Assert.Equal(AgentState.Failed, run.State);
        Assert.Contains("no such ref", run.Error);
    }

    [Fact]
    public async Task Sweep_removes_unopened_runs_for_items_that_left()
    {
        var (c, wts, _, _) = Make();
        var removed = new List<string>();
        c.RunRemoved += removed.Add;
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        c.OnItemRemoved(Item());
        await c.SweepAsync(default);
        Assert.Null(c.Get(Item().Key));
        Assert.Single(wts.Removed);
        Assert.Equal([Item().Key], removed);
    }

    [Fact]
    public async Task Sweep_keeps_recently_opened_runs_for_24h_after_item_left()
    {
        var (c, wts, _, time) = Make();
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        c.MarkOpened(Item().Key);
        c.OnItemRemoved(Item());
        time.Advance(TimeSpan.FromHours(23));
        await c.SweepAsync(default);
        Assert.NotNull(c.Get(Item().Key));
        time.Advance(TimeSpan.FromHours(2));
        await c.SweepAsync(default);
        Assert.Null(c.Get(Item().Key));
        Assert.Single(wts.Removed);
    }

    [Fact]
    public async Task Item_coming_back_clears_removal_and_marks_outdated_on_new_head()
    {
        var (c, wts, _, _) = Make();
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        c.OnItemRemoved(Item());
        c.OnItemAdded(Item(sha: "s9"));
        var run = c.Get(Item().Key)!;
        Assert.Null(run.ItemRemovedAt);
        Assert.Equal(AgentState.Outdated, run.State);
        await c.SweepAsync(default);
        Assert.NotNull(c.Get(Item().Key));
        Assert.Empty(wts.Removed);
    }

    [Fact]
    public async Task Rerun_started_before_previous_worker_returns_is_awaited_by_WhenIdle()
    {
        var (c, _, claude, _) = Make();
        var gate = new TaskCompletionSource<AgentOutcome>();
        var started = false;
        c.RunChanged += r =>
        {
            // fires on the first worker thread, before that worker's cleanup runs
            if (r.State == AgentState.Ready && !started)
            {
                started = true;
                claude.Next = () => gate.Task;
                c.TryStart(Item(), Decision(), manual: true);
            }
        };
        c.TryStart(Item(), Decision(), manual: true);
        await WaitFor(() => claude.Calls.Count == 2);
        await Task.Delay(50); // let the first worker finish its cleanup
        var idle = c.WhenIdle();
        Assert.False(idle.IsCompleted);
        gate.SetResult(new AgentOutcome(true, Sid, "second", null, null));
        await idle;
        Assert.Equal(AgentState.Ready, c.Get(Item().Key)!.State);
        Assert.Equal("second", c.Get(Item().Key)!.Summary);
        Assert.Equal(2, claude.Calls.Count);
    }

    [Fact]
    public async Task Head_change_does_not_outdate_a_queued_run()
    {
        var (c, _, claude, _) = Make(max: 1);
        var gate = new TaskCompletionSource<AgentOutcome>();
        claude.Next = () => gate.Task;
        c.TryStart(Item("a"), Decision(), true);
        await WaitFor(() => c.Get("a")!.State == AgentState.Running);
        c.TryStart(Item("b"), Decision(), true);
        c.OnItemChanged(new ItemChange(Item("b"), Item("b", sha: "s2")));
        Assert.Equal(AgentState.Queued, c.Get("b")!.State);
        gate.SetResult(new AgentOutcome(true, Sid, "ok", null, null));
        await c.WhenIdle();
    }

    [Fact]
    public async Task Sweep_does_not_remove_a_run_started_while_it_cleans_up()
    {
        var (c, wts, _, _) = Make();
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        c.OnItemRemoved(Item());

        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wts.OnRemove = async () => { entered.SetResult(); await release.Task; };
        var sweep = c.SweepAsync(default);
        await entered.Task;

        c.OnItemAdded(Item());
        Assert.True(c.TryStart(Item(), Decision(), manual: true));
        release.SetResult();
        await sweep;
        await c.WhenIdle();

        var run = c.Get(Item().Key);
        Assert.NotNull(run);
        Assert.Equal(AgentState.Ready, run!.State);
    }

    [Fact]
    public void Restored_in_flight_runs_become_failed()
    {
        var (c, _, _, _) = Make(restored:
        [
            new AgentRun { Key = "a", Profile = "peer-review", State = AgentState.Running },
            new AgentRun { Key = "b", Profile = "peer-review", State = AgentState.Ready, SessionId = Sid },
        ]);
        Assert.Equal(AgentState.Failed, c.Get("a")!.State);
        Assert.Equal("Interrupted by app restart", c.Get("a")!.Error);
        Assert.Equal(AgentState.Ready, c.Get("b")!.State);
    }
    [Fact]
    public async Task Records_result_text_and_duration()
    {
        var (c, _, claude, time) = Make();
        claude.Next = () => { time.Advance(TimeSpan.FromSeconds(42)); return Task.FromResult(new AgentOutcome(true, Sid, "s", 0.1m, null) { ResultText = "full text" }); };
        c.TryStart(Item(), Decision(), true);
        await c.WhenIdle();
        var run = c.Get(Item().Key)!;
        Assert.Equal("full text", run.ResultText);
        Assert.Equal(42, run.DurationSeconds);
    }

    [Fact]
    public async Task Command_profile_runs_the_command_not_claude()
    {
        var (c, cmds, claude, _) = MakeCmd(null);
        Assert.Null(c.WhyCannotRun(Item(), CmdDecision()));
        Assert.True(c.TryStart(Item(), CmdDecision(), manual: true));
        await c.WhenIdle();
        Assert.Equal(["triage", "{url}"], cmds.Calls.Single().Command);
        Assert.Empty(claude.Calls);
        var run = c.Get(Item().Key)!;
        Assert.Equal(AgentRunKind.Command, run.Kind);
        Assert.Equal(AgentState.Ready, run.State);
        Assert.Equal("done", run.Summary);
        Assert.Null(run.SessionId);
    }

    [Fact]
    public void Command_profile_missing_program_cannot_run()
    {
        var (c, _, _, _) = MakeCmd(null, exists: false);
        Assert.Equal("'triage' not found on PATH", c.WhyCannotRun(Item(), CmdDecision()));
        Assert.False(c.TryStart(Item(), CmdDecision(), manual: true));
    }

    [Fact]
    public async Task Command_profile_without_worktree_runs_in_located_clone()
    {
        var (c, cmds, _, _) = MakeCmd(@"C:onespi");
        Assert.True(c.TryStart(Item(), CmdDecision(), manual: true));
        await c.WhenIdle();
        Assert.Equal(@"C:onespi", cmds.Calls.Single().Cwd);
        Assert.False(c.Get(Item().Key)!.OwnsWorkingDir);
    }

    [Fact]
    public async Task Command_profile_without_clone_runs_in_scratch()
    {
        var (c, cmds, _, scratch) = MakeCmd(null);
        Assert.True(c.TryStart(Item(), CmdDecision(), manual: true));
        await c.WhenIdle();
        Assert.Equal(Path.Combine(scratch, ItemTag.For(Item().Key)), cmds.Calls.Single().Cwd);
    }

    [Fact]
    public async Task Sweeping_a_command_run_in_a_clone_keeps_the_clone()
    {
        var clone = Path.Combine(Path.GetTempPath(), "ghn-clone-" + Guid.NewGuid());
        Directory.CreateDirectory(clone);
        try
        {
            var (c, _, _, _) = MakeCmd(clone);
            Assert.True(c.TryStart(Item(), CmdDecision(), manual: true));
            await c.WhenIdle();
            c.OnItemRemoved(Item());
            await c.SweepAsync(default);
            Assert.Null(c.Get(Item().Key));
            Assert.True(Directory.Exists(clone));
        }
        finally { Directory.Delete(clone, true); }
    }
}
