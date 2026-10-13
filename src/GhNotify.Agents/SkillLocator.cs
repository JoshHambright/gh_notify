namespace GhNotify.Agents;

public interface ISkillLocator
{
    bool IsInstalled(string skill, string? repoPath);
}

public sealed class SkillLocator(string claudeHome) : ISkillLocator
{
    public static string DefaultClaudeHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public bool IsInstalled(string skill, string? repoPath)
    {
        if (File.Exists(Path.Combine(claudeHome, "skills", skill, "SKILL.md"))) return true;
        if (repoPath is not null && File.Exists(Path.Combine(repoPath, ".claude", "skills", skill, "SKILL.md"))) return true;
        var plugins = Path.Combine(claudeHome, "plugins");
        if (!Directory.Exists(plugins)) return false;
        try
        {
            return Directory.EnumerateDirectories(plugins, "skills", SearchOption.AllDirectories)
                .Any(d => File.Exists(Path.Combine(d, skill, "SKILL.md")));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
