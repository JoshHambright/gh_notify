using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ToastHoldTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-20T15:00:00Z");

    [Theory]
    [InlineData(false, false, true, false)]  // not paused, presenting but the setting is off
    [InlineData(false, true, false, false)]  // setting on, not presenting
    [InlineData(false, true, true, true)]    // setting on and presenting
    [InlineData(true, false, false, true)]   // paused by hand
    [InlineData(true, true, true, true)]
    public void Toasts_are_held_while_paused_or_while_presenting_with_the_setting_on(bool paused, bool hold, bool presenting, bool expected)
    {
        DateTimeOffset? until = paused ? Now.AddMinutes(30) : null;
        Assert.Equal(expected, ToastHold.IsHeld(until, hold, presenting, Now));
    }

    [Fact]
    public void A_pause_that_has_run_out_no_longer_holds()
    {
        Assert.False(ToastHold.IsHeld(Now.AddSeconds(-1), false, false, Now));
        Assert.False(ToastHold.IsHeld(Now, false, false, Now));
    }

    [Fact]
    public void Holding_drops_shown_toasts_and_the_sound_but_keeps_removals()
    {
        var plan = new NotificationPlan([new ToastRequest(ToastAction.Show, "a", "t", "b"), new ToastRequest(ToastAction.Remove, "b")], "builtin:chime");
        var held = ToastHold.Apply(plan, held: true);
        Assert.Equal([ToastAction.Remove], held.Toasts.Select(t => t.Action));
        Assert.Null(held.Sound);
        Assert.Same(plan, ToastHold.Apply(plan, held: false));
    }

    [Fact]
    public void The_setting_defaults_on_and_reads_from_config_json()
    {
        Assert.True(new AppConfig().Notifications.HoldWhilePresenting);
        var cfg = JsonSerializer.Deserialize<AppConfig>("""{"notifications":{"holdWhilePresenting":false}}""", Json.Options)!;
        Assert.False(cfg.Notifications.HoldWhilePresenting);
    }

    [Fact]
    public void A_manual_pause_survives_a_restart()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-hold").FullName, "state.json");
        var store = new StateStore(path);
        store.Save(new AppState { ToastsPausedUntil = Now.AddHours(1) });
        Assert.Equal(Now.AddHours(1), store.Load().ToastsPausedUntil);
        Assert.Null(new StateStore(Path.Combine(Path.GetDirectoryName(path)!, "none.json")).Load().ToastsPausedUntil);
    }
}
