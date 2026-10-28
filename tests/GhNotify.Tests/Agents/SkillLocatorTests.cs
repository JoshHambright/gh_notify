using GhNotify.Agents;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Agents;

public class SkillLocatorTests
{
    static void Skill(string dir) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: x\n---"); }

    [Fact]
    public void Finds_user_and_plugin_skills_but_not_project_skills()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        var repo = Directory.CreateTempSubdirectory("ghn-repo").FullName;
        Skill(Path.Combine(home, "skills", "peer-review"));
        Skill(Path.Combine(home, "plugins", "cache", "mkt", "plug", "1.0.0", "skills", "pr-shepherd"));
        Skill(Path.Combine(repo, ".claude", "skills", "team-review"));
        var s = new SkillLocator(home);

        Assert.True(s.IsInstalled("peer-review", null));
        Assert.True(s.IsInstalled("PR-SHEPHERD", null));
        // Agent runs use --setting-sources user, so a repo's .claude/skills are not loaded and must not count.
        Assert.False(s.IsInstalled("team-review", repo));
        Assert.False(s.IsInstalled("team-review", null));
        Assert.False(s.IsInstalled("nope", repo));
    }

    [Fact]
    public void Plugin_skills_are_cached_for_five_minutes()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var s = new SkillLocator(home, time);

        Assert.False(s.IsInstalled("late-plugin", null));

        Skill(Path.Combine(home, "plugins", "cache", "mkt", "late", "1.0.0", "skills", "late-plugin"));
        Assert.False(s.IsInstalled("late-plugin", null));

        time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.True(s.IsInstalled("late-plugin", null));
    }

    [Fact]
    public void Finds_a_repos_project_copy()
    {
        var repo = Directory.CreateTempSubdirectory("ghn-repo").FullName;
        Skill(Path.Combine(repo, ".claude", "skills", "peer-review"));
        Assert.Equal(Path.Combine(repo, ".claude", "skills", "peer-review"), SkillLocator.ProjectCopyIn(repo, "peer-review"));
        Assert.Null(SkillLocator.ProjectCopyIn(repo, "pr-shepherd"));
        Assert.Null(SkillLocator.ProjectCopyIn(null, "peer-review"));
    }

    [Fact]
    public void Installs_a_project_copy_for_the_user()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        var copy = Path.Combine(Directory.CreateTempSubdirectory("ghn-repo").FullName, ".claude", "skills", "peer-review");
        Skill(copy);
        Directory.CreateDirectory(Path.Combine(copy, "references"));
        File.WriteAllText(Path.Combine(copy, "references", "checklist.md"), "- [ ] x");
        var s = new SkillLocator(home);

        var target = s.InstallFrom(copy, "peer-review");

        Assert.Equal(Path.Combine(home, "skills", "peer-review"), target);
        Assert.True(File.Exists(Path.Combine(target, "references", "checklist.md")));
        Assert.True(s.IsInstalled("peer-review", null));
        Assert.Throws<FileNotFoundException>(() => s.InstallFrom(Path.Combine(copy, "references"), "x"));
    }

    [Fact]
    public void Default_home_follows_CLAUDE_CONFIG_DIR()
    {
        var before = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var cc = Path.Combine(Path.GetTempPath(), "ghn-cc");
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", cc);
            Assert.Equal(cc, SkillLocator.DefaultClaudeHome);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), SkillLocator.DefaultClaudeHome);
        }
        finally { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", before); }
    }

    [Fact]
    public void Directory_without_SKILL_md_does_not_count()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        Directory.CreateDirectory(Path.Combine(home, "skills", "empty"));
        Assert.False(new SkillLocator(home).IsInstalled("empty", null));
    }
}
