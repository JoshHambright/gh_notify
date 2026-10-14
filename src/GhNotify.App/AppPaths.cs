namespace GhNotify.App;

public static class AppPaths
{
    static string Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : "";
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Pre-1.2 data folder; now Velopack's install root.</summary>
    public static string LegacyDataDir => Path.Combine(LocalAppData, "GhNotify");
    public static string DataDir => Env("GHNOTIFY_DATA_DIR") is { Length: > 0 } d ? d : Path.Combine(LocalAppData, "GhNotify-data");
    public static string ConfigDir => Env("GHNOTIFY_CONFIG_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhNotify");
    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string Logs => Path.Combine(DataDir, "logs");
    public static string Worktrees => Path.Combine(DataDir, "worktrees");
    public static string Scratch => Path.Combine(DataDir, "scratch");
    public static string ToastLogo => Path.Combine(DataDir, "logo.png");
}
