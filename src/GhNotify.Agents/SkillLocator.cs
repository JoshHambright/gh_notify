namespace GhNotify.Agents;

public interface ISkillLocator
{
    bool IsInstalled(string skill, string? repoPath);

    /// <summary>The repo's own copy of a skill (<c>&lt;repo&gt;/.claude/skills/&lt;skill&gt;</c>), which agent runs don't load; null if none.</summary>
    string? ProjectCopy(string skill, string? repoPath) => SkillLocator.ProjectCopyIn(repoPath, skill);
}

public sealed class SkillLocator(string claudeHome, TimeProvider? time = null) : ISkillLocator
{
    static readonly TimeSpan PluginCacheTtl = TimeSpan.FromMinutes(5);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _gate = new();
    List<string>? _pluginSkillDirs;
    DateTimeOffset _pluginsScannedAt;

    /// <summary><c>$CLAUDE_CONFIG_DIR</c> when set (Claude Code reads user skills from there), else <c>~/.claude</c>.</summary>
    public static string DefaultClaudeHome =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>Where user skills live: <c>&lt;claude home&gt;/skills</c>.</summary>
    public string UserSkillsDir => Path.Combine(claudeHome, "skills");

    /// <remarks>Project skills (<c>&lt;repo&gt;/.claude/skills</c>) do not count: unattended runs pass
    /// <c>--setting-sources user</c> so the PR's own .claude settings cannot apply, and that also stops project skills
    /// loading. <paramref name="repoPath"/> is kept for callers but ignored.</remarks>
    public bool IsInstalled(string skill, string? repoPath)
    {
        if (File.Exists(Path.Combine(UserSkillsDir, skill, "SKILL.md"))) return true;
        return PluginSkillDirs().Any(d => File.Exists(Path.Combine(d, skill, "SKILL.md")));
    }

    public static string? ProjectCopyIn(string? repoPath, string skill)
    {
        if (repoPath is null) return null;
        var dir = Path.Combine(repoPath, ".claude", "skills", skill);
        return File.Exists(Path.Combine(dir, "SKILL.md")) ? dir : null;
    }

    /// <summary>Copies a skill folder (usually a repo's project copy) into <see cref="UserSkillsDir"/>; returns the new folder.
    /// The copy is the user's from then on: updating the repo's copy doesn't update it. It is staged beside the target and moved
    /// into place only once complete, so a failed copy never leaves a SKILL.md that counts as installed.</summary>
    public string InstallFrom(string sourceDir, string skill)
    {
        if (!File.Exists(Path.Combine(sourceDir, "SKILL.md"))) throw new FileNotFoundException($"No SKILL.md in {sourceDir}");
        var target = Path.Combine(UserSkillsDir, skill);
        if (File.Exists(Path.Combine(target, "SKILL.md"))) throw new IOException($"{target} already has a SKILL.md");
        var staging = Path.Combine(UserSkillsDir, $".{skill}.installing-{Guid.NewGuid():N}");
        try
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(staging, Path.GetRelativePath(sourceDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
            }
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // a leftover with no SKILL.md
            Directory.Move(staging, target);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
        return target;
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
