using GhNotify.Platform.Windows;

namespace GhNotify.Platform.Windows.Tests;

public class WinPresenceTests
{
    [Theory]
    [InlineData(1, false)] // QUNS_NOT_PRESENT: screen saver or locked; toasts go nowhere anyway
    [InlineData(2, true)]  // QUNS_BUSY: a full-screen app
    [InlineData(3, true)]  // QUNS_RUNNING_D3D_FULL_SCREEN
    [InlineData(4, true)]  // QUNS_PRESENTATION_MODE
    [InlineData(5, false)] // QUNS_ACCEPTS_NOTIFICATIONS
    [InlineData(6, false)] // QUNS_QUIET_TIME: Windows already holds toasts itself
    [InlineData(7, false)] // QUNS_APP
    [InlineData(0, false)]
    public void Only_busy_full_screen_and_presentation_states_count_as_presenting(int state, bool expected) =>
        Assert.Equal(expected, WinPresenceService.IsPresentingState(state));

    [Fact]
    public void Reading_the_real_state_never_throws() => _ = new WinPresenceService().IsPresenting;
}
