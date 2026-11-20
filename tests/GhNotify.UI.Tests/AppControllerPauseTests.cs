using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Pausing toasts by hand, and holding them while the OS says the user is presenting.</summary>
public sealed class AppControllerPauseTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("ghn-pause").FullName;
    readonly FakePlatform _platform = new();
    readonly StateStore _state;
    readonly ConfigStore _config;

    public AppControllerPauseTests()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, """{"github":{"enabled":false},"ado":{"enabled":false}}""");
        _config = new ConfigStore(configPath);
        _config.Load();
        _state = new StateStore(Path.Combine(_root, "state.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    AppController Make()
    {
        var dirs = new AppDirs(_root, Path.Combine(_root, "logs"), Path.Combine(_root, "wt"), Path.Combine(_root, "scratch"));
        return new AppController(_config, _state, _platform.Services, new AppRuntime(new InlineDispatcher(), dirs, () => { }, _ => { }));
    }

    [Fact]
    public void Pausing_holds_toasts_resuming_releases_them_and_the_pause_is_saved()
    {
        using var controller = Make();
        Assert.False(controller.ToastsHeld);
        controller.PauseToasts(TimeSpan.FromHours(1));
        Assert.True(controller.ToastsHeld);
        Assert.NotNull(controller.ToastsPausedUntil);
        Assert.NotNull(_state.Load().ToastsPausedUntil);

        controller.ResumeToasts();
        Assert.False(controller.ToastsHeld);
        Assert.Null(controller.ToastsPausedUntil);
        Assert.Null(_state.Load().ToastsPausedUntil);
    }

    [Fact]
    public void A_pause_from_the_last_run_is_still_in_force_after_a_restart()
    {
        _state.Save(new AppState { ToastsPausedUntil = DateTimeOffset.Now.AddMinutes(20) });
        using var controller = Make();
        Assert.True(controller.ToastsHeld);
    }

    [Fact]
    public void An_expired_pause_from_the_last_run_does_not_hold()
    {
        _state.Save(new AppState { ToastsPausedUntil = DateTimeOffset.Now.AddMinutes(-1) });
        using var controller = Make();
        Assert.False(controller.ToastsHeld);
    }

    [Fact]
    public void Presenting_holds_toasts_only_while_the_setting_is_on()
    {
        _platform.Presence.Presenting = true;
        using var controller = Make();
        Assert.True(controller.ToastsHeld);

        File.WriteAllText(Path.Combine(_root, "config.json"), """{"github":{"enabled":false},"ado":{"enabled":false},"notifications":{"holdWhilePresenting":false}}""");
        _config.Load();
        Assert.False(controller.ToastsHeld);
    }

    [Fact]
    public void A_platform_without_presence_detection_never_holds_by_itself()
    {
        using var controller = Make(); // FakePresence says false by default
        Assert.False(controller.ToastsHeld);
    }
}
