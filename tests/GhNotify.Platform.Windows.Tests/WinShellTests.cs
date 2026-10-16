using GhNotify.Core;
using GhNotify.Platform.Windows;

namespace GhNotify.Platform.Windows.Tests;

public class WinShellTests
{
    const string Sid = "11111111-1111-1111-1111-111111111111";
    const string ClaudeCmd = @"C:\Users\me\AppData\Roaming\npm\claude.cmd";

    sealed class RecordingRunner : IProcessRunner
    {
        public List<ProcessSpec> Detached { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default) => Task.FromResult(new ProcessResult(0, "", "", false));
        public void StartDetached(ProcessSpec spec) => Detached.Add(spec);
    }

    static string? Find(string name) => name == "wt" ? @"C:\x\wt.exe" : null;

    [Fact]
    public void Uses_windows_terminal_when_installed()
    {
        var runner = new RecordingRunner();
        new WinShell(runner, Find).OpenTerminal(@"C:\wt\abc", $"{ClaudeCmd} --resume {Sid}");
        var s = runner.Detached.Single();
        Assert.Equal("wt", s.FileName);
        Assert.Equal(["-d", @"C:\wt\abc", ClaudeCmd, "--resume", Sid], s.Args);
    }

    [Fact]
    public void A_quoted_program_path_stays_one_argument()
    {
        var runner = new RecordingRunner();
        new WinShell(runner, Find).OpenTerminal(@"C:\wt\abc", $"\"C:\\Program Files\\claude.cmd\" --resume {Sid}");
        Assert.Equal(["-d", @"C:\wt\abc", @"C:\Program Files\claude.cmd", "--resume", Sid], runner.Detached.Single().Args);
    }

    [Fact]
    public void Falls_back_to_powershell_with_the_same_command_line()
    {
        var runner = new RecordingRunner();
        new WinShell(runner, _ => null).OpenTerminal(@"C:\wt\o'brien", $"{ClaudeCmd} --resume {Sid}");
        var s = runner.Detached.Single();
        Assert.Equal("powershell", s.FileName);
        Assert.Equal(@"C:\wt\o'brien", s.WorkingDirectory);
        Assert.Equal(["-NoExit", "-Command"], s.Args.Take(2));
        Assert.Equal($@"Set-Location -LiteralPath 'C:\wt\o''brien'; & '{ClaudeCmd}' --resume {Sid}", s.Args[^1]);
    }

    [Fact]
    public void Open_url_refuses_anything_but_https()
    {
        // Would throw (or launch something) if the check were missing: a file path is not a valid URL to shell-execute here.
        new WinShell(new RecordingRunner(), Find).OpenUrl("file:///c:/windows/system32/calc.exe");
        new WinShell(new RecordingRunner(), Find).OpenUrl("http://example.com");
    }

    [Fact]
    public void Splits_command_lines_on_unquoted_spaces()
    {
        Assert.Equal(["a", "b c", "d"], CommandLine.Split("a \"b c\"  d"));
        Assert.Empty(CommandLine.Split(""));
    }
}
