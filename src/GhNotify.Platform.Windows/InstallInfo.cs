namespace GhNotify.App;

public static class InstallInfo
{
    /// <summary>The exe path that survives updates when running from a Velopack install; null for dev builds.</summary>
    public static string? StablePathFor(string processPath)
    {
        var dir = Path.GetDirectoryName(processPath);
        if (dir is null || !string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Path.GetDirectoryName(dir);
        return root is not null && File.Exists(Path.Combine(root, "Update.exe")) ? processPath : null;
    }

    public static string? StableExePath => Environment.ProcessPath is { } p ? StablePathFor(p) : null;
    public static bool IsInstalled => StableExePath is not null;
}
