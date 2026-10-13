namespace GhNotify.App;

public static class AppPaths
{
    static string Local => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhNotify");
    public static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhNotify");
    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
    public static string StateFile => Path.Combine(Local, "state.json");
    public static string Logs => Path.Combine(Local, "logs");
    public static string Worktrees => Path.Combine(Local, "worktrees");
    public static string Scratch => Path.Combine(Local, "scratch");
    public static string ToastLogo => Path.Combine(Local, "logo.png");
}
