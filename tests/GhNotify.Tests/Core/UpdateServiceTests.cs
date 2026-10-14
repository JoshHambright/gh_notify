using GhNotify.Core;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Core;

public class UpdateServiceTests
{
    sealed class FakeClient : IUpdateClient
    {
        public bool IsInstalled { get; set; } = true;
        public string? CurrentVersion { get; set; } = "1.2.0";
        public UpdateCandidate? Next;
        public Exception? Fail;
        public int Checks, Downloads;
        public UpdateCandidate? Applied;
        public Task<UpdateCandidate?> CheckAsync(CancellationToken ct) { Checks++; return Fail is null ? Task.FromResult(Next) : Task.FromException<UpdateCandidate?>(Fail); }
        public Task DownloadAsync(UpdateCandidate c, CancellationToken ct) { Downloads++; return Task.CompletedTask; }
        public void ApplyOnExit(UpdateCandidate c) => Applied = c;
    }

    static UpdateService Svc(FakeClient c, UpdatesConfig? cfg = null, TimeProvider? t = null) => new(c, () => cfg ?? new UpdatesConfig(), t);

    [Fact]
    public async Task Dev_build_never_checks()
    {
        var c = new FakeClient { IsInstalled = false };
        var s = Svc(c);
        await s.CheckNowAsync(default);
        Assert.Equal(UpdatePhase.DevBuild, s.Status.Phase);
        Assert.Equal(0, c.Checks);
    }

    [Fact]
    public async Task Disabled_never_checks()
    {
        var c = new FakeClient();
        var s = Svc(c, new UpdatesConfig { Enabled = false });
        await s.CheckNowAsync(default);
        Assert.Equal(UpdatePhase.Off, s.Status.Phase);
        Assert.Equal(0, c.Checks);
    }

    [Fact]
    public async Task Found_update_downloads_and_is_ready()
    {
        var c = new FakeClient { Next = new UpdateCandidate("1.2.1", new object()) };
        var s = Svc(c);
        await s.CheckNowAsync(default);
        Assert.Equal(UpdatePhase.Ready, s.Status.Phase);
        Assert.Equal("1.2.1", s.Status.ReadyVersion);
        Assert.Equal(1, c.Downloads);
    }

    [Fact]
    public async Task No_update_is_idle_with_last_checked()
    {
        var s = Svc(new FakeClient());
        await s.CheckNowAsync(default);
        Assert.Equal(UpdatePhase.Idle, s.Status.Phase);
        Assert.NotNull(s.Status.LastChecked);
    }

    [Fact]
    public async Task Failure_is_reported_not_thrown()
    {
        var s = Svc(new FakeClient { Fail = new HttpRequestException("offline") });
        await s.CheckNowAsync(default);
        Assert.Equal(UpdatePhase.Failed, s.Status.Phase);
        Assert.Contains("offline", s.Status.LastError);
    }

    [Fact]
    public async Task Restart_requires_confirm_when_agents_running()
    {
        var c = new FakeClient { Next = new UpdateCandidate("1.2.1", new object()) };
        var s = Svc(c);
        await s.CheckNowAsync(default);
        Assert.Equal(RestartDecision.NeedsConfirm, s.RequestRestart(runningAgents: 2, confirmed: false));
        Assert.Null(c.Applied);
        Assert.Equal(RestartDecision.Restarting, s.RequestRestart(runningAgents: 2, confirmed: true));
        Assert.Equal("1.2.1", c.Applied!.Version);
    }

    [Fact]
    public void Restart_without_ready_update_does_nothing() =>
        Assert.Equal(RestartDecision.NothingToApply, Svc(new FakeClient()).RequestRestart(0, true));

    [Fact]
    public async Task Schedule_first_check_after_30s_then_every_check_hours()
    {
        var time = new FakeTimeProvider();
        var c = new FakeClient();
        var s = Svc(c, new UpdatesConfig { CheckHours = 4 }, time);
        using var cts = new CancellationTokenSource();
        var run = s.RunAsync(cts.Token);
        time.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        Assert.Equal(0, c.Checks);
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitFor(() => c.Checks == 1 && s.Status.Phase == UpdatePhase.Idle);
        await Task.Delay(50); // let RunAsync register its 4 h delay before time moves
        time.Advance(TimeSpan.FromHours(4));
        await WaitFor(() => c.Checks == 2);
        cts.Cancel();
        await run;
    }

    static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 200 && !cond(); i++) await Task.Delay(5);
        Assert.True(cond());
    }
}
