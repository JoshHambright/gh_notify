namespace GhNotify.App.Mac;

/// <summary>Where gh_notify keeps its files on macOS. GHNOTIFY_CONFIG_DIR and GHNOTIFY_DATA_DIR override them, as on Windows.</summary>
public sealed class MacPaths(string home, Func<string, string?> env)
{
    public static MacPaths Current { get; } =
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable);

    string AppSupport => Path.Combine(home, "Library", "Application Support");
    string? Env(string name) => env(name) is { Length: > 0 } v ? v : null;

    public string ConfigDir => Env("GHNOTIFY_CONFIG_DIR") ?? Path.Combine(AppSupport, "GhNotify");
    public string DataDir => Env("GHNOTIFY_DATA_DIR") ?? Path.Combine(AppSupport, "GhNotify-data");
    public string ConfigFile => Path.Combine(ConfigDir, "config.json");
    public string StateFile => Path.Combine(DataDir, "state.json");
    /// <summary>~/Library/Logs/GhNotify, where Console.app looks; under the data folder when GHNOTIFY_DATA_DIR is set (tests).</summary>
    public string Logs => Env("GHNOTIFY_DATA_DIR") is { } d ? Path.Combine(d, "logs") : Path.Combine(home, "Library", "Logs", "GhNotify");
    public string Worktrees => Path.Combine(DataDir, "worktrees");
    public string Scratch => Path.Combine(DataDir, "scratch");
    public string Sounds => Path.Combine(DataDir, "sounds");
    public string InstanceLock => Path.Combine(DataDir, "instance.lock");
}
