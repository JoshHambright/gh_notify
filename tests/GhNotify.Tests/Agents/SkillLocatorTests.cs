using GhNotify.Agents;

namespace GhNotify.Tests.Agents;

public class SkillLocatorTests
{
    static void Skill(string dir) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: x\n---"); }

    [Fact]
    public void Finds_user_project_and_plugin_skills()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        var repo = Directory.CreateTempSubdirectory("ghn-repo").FullName;
        Skill(Path.Combine(home, "skills", "peer-review"));
        Skill(Path.Combine(home, "plugins", "cache", "mkt", "plug", "1.0.0", "skills", "pr-shepherd"));
        Skill(Path.Combine(repo, ".claude", "skills", "team-review"));
        var s = new SkillLocator(home);

        Assert.True(s.IsInstalled("peer-review", null));
        Assert.True(s.IsInstalled("PR-SHEPHERD", null));
        Assert.True(s.IsInstalled("team-review", repo));
        Assert.False(s.IsInstalled("team-review", null));
        Assert.False(s.IsInstalled("nope", repo));
    }

    [Fact]
    public void Directory_without_SKILL_md_does_not_count()
    {
        var home = Directory.CreateTempSubdirectory("ghn-claude").FullName;
        Directory.CreateDirectory(Path.Combine(home, "skills", "empty"));
        Assert.False(new SkillLocator(home).IsInstalled("empty", null));
    }
}
