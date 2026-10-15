using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class CommandResolverTests
{
    static Func<string, bool> Files(params string[] files) => p => files.Contains(p.Replace('\\', '/'));

    [Fact]
    public void Windows_tries_exe_cmd_bat_on_a_semicolon_path()
    {
        var found = CommandResolver.Find("claude", @"C:\a;C:\npm", Files("C:/npm/claude.cmd"), windows: true);
        Assert.Equal(Path.Combine(@"C:\npm", "claude.cmd"), found);
    }

    [Fact]
    public void Windows_keeps_an_explicit_extension() =>
        Assert.Null(CommandResolver.Find("tool.sh", @"C:\a", Files("C:/a/tool.sh.exe"), windows: true));

    [Fact]
    public void Unix_uses_the_bare_name_on_a_colon_path()
    {
        var found = CommandResolver.Find("claude", "/usr/bin:/opt/x", Files("/opt/x/claude"), windows: false);
        Assert.Equal(Path.Combine("/opt/x", "claude"), found);
    }

    [Fact]
    public void Unix_does_not_append_extensions() =>
        Assert.Null(CommandResolver.Find("claude", "/usr/bin", Files("/usr/bin/claude.exe", "/usr/bin/claude.cmd"), windows: false));

    [Fact]
    public void Unix_ignores_windows_separators_in_the_path() =>
        Assert.Null(CommandResolver.Find("claude", "/a;/b", Files("/b/claude"), windows: false));

    [Fact]
    public void A_rooted_name_is_checked_directly()
    {
        Assert.Equal("/bin/x", CommandResolver.Find("/bin/x", null, Files("/bin/x"), windows: false));
        Assert.Null(CommandResolver.Find("/bin/y", null, Files("/bin/x"), windows: false));
    }

    [Fact]
    public void Unix_runs_sh_scripts_through_bin_sh()
    {
        var psi = CommandResolver.CreateStartInfoFor("/opt/hooks/notify.sh", ["a b", "c"], windows: false);
        Assert.Equal("/bin/sh", psi.FileName);
        Assert.Equal(["/opt/hooks/notify.sh", "a b", "c"], psi.ArgumentList);
    }

    [Fact]
    public void Unix_runs_other_files_directly()
    {
        var psi = CommandResolver.CreateStartInfoFor("/usr/bin/git", ["status"], windows: false);
        Assert.Equal("/usr/bin/git", psi.FileName);
        Assert.Equal(["status"], psi.ArgumentList);
    }

    [Fact]
    public void Windows_runs_cmd_scripts_via_cmd_exe()
    {
        var psi = CommandResolver.CreateStartInfoFor(@"C:\npm\claude.cmd", ["--resume", "x"], windows: true);
        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal("/d /s /c \"\"C:\\npm\\claude.cmd\" \"--resume\" \"x\"\"", psi.Arguments);
    }

    [Fact]
    public void Windows_rejects_unsafe_cmd_arguments() =>
        Assert.Throws<ArgumentException>(() => CommandResolver.CreateStartInfoFor(@"C:\a.cmd", ["50%"], windows: true));

    [Fact]
    public void Default_repo_root_depends_on_the_os()
    {
        Assert.Equal(@"C:\dev", AppConfig.DefaultRepoRoot(windows: true, home: "/home/me"));
        Assert.Equal(Path.Combine("/home/me", "dev"), AppConfig.DefaultRepoRoot(windows: false, home: "/home/me"));
    }
}
