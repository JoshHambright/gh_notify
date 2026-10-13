using GhNotify.Core;

namespace GhNotify.Agents;

public interface IWorktreeManager
{
    Task<string> CreateAsync(string clonePath, string fetchRef, string tag, CancellationToken ct);
    Task RemoveAsync(string clonePath, string worktreePath, string tag, CancellationToken ct);
}

public sealed class GitException(string message) : Exception(message);

public sealed class WorktreeManager(IProcessRunner runner, string baseDir) : IWorktreeManager
{
    public async Task<string> CreateAsync(string clonePath, string fetchRef, string tag, CancellationToken ct)
    {
        var wt = Path.Combine(baseDir, tag);
        if (Directory.Exists(wt)) await RemoveAsync(clonePath, wt, tag, ct);
        var localRef = $"refs/ghnotify/{tag}";
        var src = fetchRef.StartsWith("refs/", StringComparison.Ordinal) ? fetchRef
            : fetchRef.StartsWith("pull/", StringComparison.Ordinal) ? "refs/" + fetchRef
            : "refs/heads/" + fetchRef;
        await Git(clonePath, ct, "fetch", "--no-tags", "origin", $"+{src}:{localRef}");
        Directory.CreateDirectory(baseDir);
        await Git(clonePath, ct, "worktree", "add", "--detach", wt, localRef);
        return wt;
    }

    public async Task RemoveAsync(string clonePath, string worktreePath, string tag, CancellationToken ct)
    {
        await TryGit(clonePath, ct, "worktree", "remove", "--force", worktreePath);
        if (Directory.Exists(worktreePath)) ForceDelete(worktreePath);
        await TryGit(clonePath, ct, "worktree", "prune");
        await TryGit(clonePath, ct, "update-ref", "-d", $"refs/ghnotify/{tag}");
    }

    async Task Git(string repo, CancellationToken ct, params string[] args)
    {
        var r = await runner.RunAsync(new ProcessSpec("git", ["-C", repo, .. args], Timeout: TimeSpan.FromMinutes(5)), ct);
        if (r.ExitCode != 0 || r.TimedOut)
            throw new GitException($"git {string.Join(' ', args)} failed: {(r.TimedOut ? "timed out" : r.StdErr.Trim())}");
    }

    async Task TryGit(string repo, CancellationToken ct, params string[] args)
    {
        try { await Git(repo, ct, args); }
        catch (GitException ex) { Log.Info(ex.Message); }
    }

    static void ForceDelete(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"could not delete {dir}", ex);
        }
    }
}
