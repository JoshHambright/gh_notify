using GhNotify.Agents;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Agents;

public class ClaudeLauncherTests
{
    const string Sid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Uses_windows_terminal_when_available()
    {
        var runner = new FakeProcessRunner();
        new ClaudeLauncher(runner, _ => @"C:\x\wt.exe").Resume(@"C:\wt\abc", Sid);
        var s = runner.Detached.Single();
        Assert.Equal("wt", s.FileName);
        Assert.Equal(["-d", @"C:\wt\abc", "claude", "--resume", Sid], s.Args);
    }

    [Fact]
    public void Falls_back_to_powershell()
    {
        var runner = new FakeProcessRunner();
        new ClaudeLauncher(runner, _ => null).Resume(@"C:\wt\o'brien", Sid);
        var s = runner.Detached.Single();
        Assert.Equal("powershell", s.FileName);
        Assert.Equal(@"C:\wt\o'brien", s.WorkingDirectory);
        Assert.Equal($"Set-Location -LiteralPath 'C:\\wt\\o''brien'; claude --resume {Sid}", s.Args[^1]);
    }

    [Fact]
    public void Rejects_non_guid_session_ids() =>
        Assert.Throws<ArgumentException>(() => new ClaudeLauncher(new FakeProcessRunner(), _ => null).Resume(@"C:\x", "abc; rm -rf"));
}
