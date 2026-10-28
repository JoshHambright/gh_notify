using GhNotify.Core;

namespace GhNotify.Agents;

public interface ILocalRepoIndex
{
    string? FindClone(string cloneUrl);
}

public sealed class RepoLocator(Func<IReadOnlyList<string>> roots, int maxDepth = 3, TimeProvider? time = null) : ILocalRepoIndex
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "packages" };
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _gate = new();
    Dictionary<string, string>? _map;
    DateTimeOffset _scannedAt;

    public string? FindClone(string cloneUrl)
    {
        var key = RemoteUrl.Normalize(cloneUrl);
        if (key is null) return null;
        lock (_gate)
        {
            var age = _time.GetUtcNow() - _scannedAt;
            var rescanned = false;
            if (_map is null || age > TimeSpan.FromHours(1))
            {
                Rescan();
                rescanned = true;
            }
            if (_map!.TryGetValue(key, out var path)) return path;
            if (!rescanned && age > TimeSpan.FromMinutes(5))
            {
                Rescan();
                return _map.GetValueOrDefault(key);
            }
            return null;
        }
    }

    /// <summary>Every main clone found under repoRoots (scanning first if the last scan is over an hour old).</summary>
    public IReadOnlyList<string> Clones()
    {
        lock (_gate)
        {
            if (_map is null || _time.GetUtcNow() - _scannedAt > TimeSpan.FromHours(1)) Rescan();
            return _map!.Values.ToList();
        }
    }

    public void Rescan()
    {
        var map = new Dictionary<string, string>();
        IReadOnlyList<string>? list;
        try { list = roots(); }
        catch (Exception ex)
        {
            Log.Error("repoRoots could not be read; scanning no roots", ex);
            list = null;
        }
        if (list is null) Log.Info("no repoRoots configured; no local clones will be found");
        foreach (var root in list ?? [])
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) Walk(root, 0, map);
        lock (_gate)
        {
            _map = map;
            _scannedAt = _time.GetUtcNow();
        }
        Log.Info($"repo scan found {map.Count} clones");
    }

    void Walk(string dir, int depth, Dictionary<string, string> map)
    {
        var dotGit = Path.Combine(dir, ".git");
        if (Directory.Exists(dotGit))
        {
            if (ReadOriginUrl(Path.Combine(dotGit, "config")) is { } url && RemoteUrl.Normalize(url) is { } key)
                map.TryAdd(key, dir);
            return;
        }
        if (File.Exists(dotGit) || depth >= maxDepth) return; // .git file = worktree/submodule, not a main clone
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }
        foreach (var sub in subs)
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || Skip.Contains(name)) continue;
            Walk(sub, depth + 1, map);
        }
    }

    public static string? ReadOriginUrl(string gitConfigPath)
    {
        if (!File.Exists(gitConfigPath)) return null;
        try
        {
            var inOrigin = false;
            foreach (var raw in File.ReadLines(gitConfigPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inOrigin = line.Replace(" ", "").Equals("[remote\"origin\"]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inOrigin && line.StartsWith("url", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is var eq and > 0)
                    return line[(eq + 1)..].Trim();
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // locked or unreadable config: skip this clone, keep scanning
        }
    }
}
