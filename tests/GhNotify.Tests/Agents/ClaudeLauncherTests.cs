using GhNotify.Agents;

namespace GhNotify.Tests.Agents;

public class ClaudeLauncherTests
{
    const string Sid = "11111111-1111-1111-1111-111111111111";
    const string ClaudeCmd = @"C:\Users\me\AppData\Roaming\npm\claude.cmd";

    [Fact]
    public void Asks_the_terminal_to_resume_with_the_resolved_claude_path()
    {
        var calls = new List<(string Dir, string Command)>();
        new ClaudeLauncher((d, c) => calls.Add((d, c)), n => n == "claude" ? ClaudeCmd : null).Resume(@"C:\wt\abc", Sid);
        Assert.Equal([(@"C:\wt\abc", $"{ClaudeCmd} --resume {Sid}")], calls);
    }

    [Fact]
    public void Quotes_a_claude_path_with_spaces()
    {
        var calls = new List<string>();
        new ClaudeLauncher((_, c) => calls.Add(c), _ => "/opt/my tools/claude").Resume("/wt/abc", Sid);
        Assert.Equal($"\"/opt/my tools/claude\" --resume {Sid}", calls.Single());
    }

    [Fact]
    public void Uses_bare_claude_when_not_found_on_path()
    {
        var calls = new List<string>();
        new ClaudeLauncher((_, c) => calls.Add(c), _ => null).Resume("/wt/abc", Sid);
        Assert.Equal($"claude --resume {Sid}", calls.Single());
    }

    [Fact]
    public void Rejects_non_guid_session_ids()
    {
        var called = false;
        Assert.Throws<ArgumentException>(() => new ClaudeLauncher((_, _) => called = true, _ => null).Resume(@"C:\x", "abc; rm -rf"));
        Assert.False(called);
    }
}
