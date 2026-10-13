using GhNotify.Agents;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Agents;

public class ClaudeLauncherTests
{
    const string Sid = "11111111-1111-1111-1111-111111111111";
    const string ClaudeCmd = @"C:\Users\me\AppData\Roaming\npm\claude.cmd";

    static string? Find(string name, bool wt) => name switch
    {
        "wt" => wt ? @"C:\x\wt.exe" : null,
        "claude" => ClaudeCmd,
        _ => null,
    };

    [Fact]
    public void Uses_windows_terminal_with_resolved_claude_path()
    {
        var runner = new FakeProcessRunner();
        new ClaudeLauncher(runner, n => Find(n, wt: true)).Resume(@"C:\wt\abc", Sid);
        var s = runner.Detached.Single();
        Assert.Equal("wt", s.FileName);
        Assert.Equal(["-d", @"C:\wt\abc", ClaudeCmd, "--resume", Sid], s.Args);
    }

    [Fact]
    public void Falls_back_to_powershell_with_resolved_claude_path()
    {
        var runner = new FakeProcessRunner();
        new ClaudeLauncher(runner, n => Find(n, wt: false)).Resume(@"C:\wt\o'brien", Sid);
        var s = runner.Detached.Single();
        Assert.Equal("powershell", s.FileName);
        Assert.Equal(@"C:\wt\o'brien", s.WorkingDirectory);
        Assert.Equal($@"Set-Location -LiteralPath 'C:\wt\o''brien'; & '{ClaudeCmd}' --resume {Sid}", s.Args[^1]);
    }

    [Fact]
    public void Uses_bare_claude_when_not_found_on_path()
    {
        var runner = new FakeProcessRunner();
        new ClaudeLauncher(runner, n => n == "wt" ? @"C:\x\wt.exe" : null).Resume(@"C:\wt\abc", Sid);
        Assert.Equal(["-d", @"C:\wt\abc", "claude", "--resume", Sid], runner.Detached.Single().Args);
    }

    [Fact]
    public void Rejects_non_guid_session_ids() =>
        Assert.Throws<ArgumentException>(() => new ClaudeLauncher(new FakeProcessRunner(), _ => null).Resume(@"C:\x", "abc; rm -rf"));
}
