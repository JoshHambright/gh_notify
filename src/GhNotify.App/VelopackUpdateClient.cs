using GhNotify.Core;
using Velopack;
using Velopack.Sources;

namespace GhNotify.App;

public sealed class VelopackUpdateClient : IUpdateClient
{
    readonly UpdateManager _mgr;

    public VelopackUpdateClient()
    {
        IUpdateSource source;
        if (Environment.GetEnvironmentVariable("GHNOTIFY_UPDATE_FEED") is { Length: > 0 } feed)
        {
            Log.Info($"update feed overridden by GHNOTIFY_UPDATE_FEED: {feed}");
            source = new SimpleFileSource(new DirectoryInfo(feed));
        }
        else source = new GithubSource("https://github.com/JoshHambright/gh_notify", null, false);
        _mgr = new UpdateManager(source);
    }

    public bool IsInstalled => _mgr.IsInstalled;
    public string? CurrentVersion => _mgr.CurrentVersion?.ToString();

    public async Task<UpdateCandidate?> CheckAsync(CancellationToken ct)
    {
        var info = await _mgr.CheckForUpdatesAsync();
        return info is null ? null : new UpdateCandidate(info.TargetFullRelease.Version.ToString(), info);
    }

    public Task DownloadAsync(UpdateCandidate c, CancellationToken ct) =>
        _mgr.DownloadUpdatesAsync((UpdateInfo)c.Native, cancelToken: ct);

    public void ApplyOnExit(UpdateCandidate c) =>
        _mgr.WaitExitThenApplyUpdates(((UpdateInfo)c.Native).TargetFullRelease, silent: true, restart: true);
}
