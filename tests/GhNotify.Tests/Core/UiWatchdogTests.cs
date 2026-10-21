using GhNotify.Core;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Core;

public class UiWatchdogTests
{
    readonly FakeTimeProvider _time = new();
    readonly List<Action> _posted = [];

    UiWatchdog Make()
    {
        var w = new UiWatchdog(_posted.Add, TimeSpan.FromSeconds(10), _time);
        w.Start();
        return w;
    }

    [Fact]
    public void A_responsive_UI_thread_is_never_stalled()
    {
        using var w = Make();
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(UiWatchdog.Interval);
            foreach (var a in _posted.ToList()) a();
            _posted.Clear();
            Assert.False(w.Stalled);
        }
    }

    [Fact]
    public void An_unanswered_ping_is_a_stall_until_the_UI_thread_answers()
    {
        using var w = Make();
        _time.Advance(UiWatchdog.Interval);
        Assert.Single(_posted);

        _time.Advance(TimeSpan.FromSeconds(8));
        Assert.False(w.Stalled);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(w.Stalled);
        Assert.Single(_posted); // no second ping piles up while the first is waiting

        _posted[0]();
        Assert.False(w.Stalled);
        _time.Advance(UiWatchdog.Interval);
        Assert.Equal(2, _posted.Count);
    }
}
