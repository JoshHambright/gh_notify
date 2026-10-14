using Avalonia;
using Avalonia.Headless;
using GhNotify.UI;
using GhNotify.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace GhNotify.UI.Tests;

/// <summary>Per-assembly headless application: the real <see cref="App"/> wired to fake platform services and a temp data folder.</summary>
public static class TestAppBuilder
{
    public static FakePlatform Platform { get; } = new();
    public static string Root { get; } = Directory.CreateTempSubdirectory("ghn-ui-tests").FullName;

    public static AppOptions Options()
    {
        var data = Path.Combine(Root, "data");
        var config = Path.Combine(Root, "config", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        // No sources: the smoke test must never call gh or az.
        if (!File.Exists(config)) File.WriteAllText(config, """{"github":{"enabled":false},"ado":{"enabled":false}}""");
        var dirs = new AppDirs(data, Path.Combine(data, "logs"), Path.Combine(data, "worktrees"), Path.Combine(data, "scratch"));
        return new AppOptions(Platform.Services, dirs, config, Path.Combine(data, "state.json"), "0.0.0-test", new OffUpdateClient());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new App(Options())).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>An update client for a dev build: not installed, so UpdateService stays off.</summary>
sealed class OffUpdateClient : GhNotify.Core.IUpdateClient
{
    public bool IsInstalled => false;
    public string? CurrentVersion => null;
    public Task<GhNotify.Core.UpdateCandidate?> CheckAsync(CancellationToken ct) => Task.FromResult<GhNotify.Core.UpdateCandidate?>(null);
    public Task DownloadAsync(GhNotify.Core.UpdateCandidate c, CancellationToken ct) => Task.CompletedTask;
    public void ApplyOnExit(GhNotify.Core.UpdateCandidate c) { }
}
