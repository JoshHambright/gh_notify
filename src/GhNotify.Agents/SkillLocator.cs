namespace GhNotify.Agents;

public interface ISkillLocator
{
    bool IsInstalled(string skill, string? repoPath);
}

public sealed class SkillLocator(string claudeHome, TimeProvider? time = null) : ISkillLocator
{
    static readonly TimeSpan PluginCacheTtl = TimeSpan.FromMinutes(5);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _gate = new();
    List<string>? _pluginSkillDirs;
    DateTimeOffset _pluginsScannedAt;

    public static string DefaultClaudeHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public bool IsInstalled(string skill, string? repoPath)
    {
        if (File.Exists(Path.Combine(claudeHome, "skills", skill, "SKILL.md"))) return true;
        if (repoPath is not null && File.Exists(Path.Combine(repoPath, ".claude", "skills", skill, "SKILL.md"))) return true;
        return PluginSkillDirs().Any(d => File.Exists(Path.Combine(d, skill, "SKILL.md")));
    }

    /// <summary>The plugin "skills" directories, re-enumerated at most every 5 minutes (called per popup row).</summary>
    List<string> PluginSkillDirs()
    {
        lock (_gate)
        {
            if (_pluginSkillDirs is null || _time.GetUtcNow() - _pluginsScannedAt > PluginCacheTtl)
            {
                _pluginSkillDirs = EnumeratePluginSkillDirs(Path.Combine(claudeHome, "plugins"));
                _pluginsScannedAt = _time.GetUtcNow();
            }
            return _pluginSkillDirs;
        }
    }

    static List<string> EnumeratePluginSkillDirs(string plugins)
    {
        if (!Directory.Exists(plugins)) return [];
        try
        {
            return Directory.EnumerateDirectories(plugins, "skills", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
