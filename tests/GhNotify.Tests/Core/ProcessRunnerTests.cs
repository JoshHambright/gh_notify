using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ProcessRunnerTests
{
    readonly ProcessRunner _runner = new();

    [WindowsFact]
    public async Task Captures_stdout_and_exit_code()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd", ["/c", "echo hello"]));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello", r.StdOut);
    }

    [WindowsFact]
    public async Task Writes_stdin()
    {
        var r = await _runner.RunAsync(new ProcessSpec("findstr", ["x"], StdIn: "x1\ny2\n"));
        Assert.Contains("x1", r.StdOut);
        Assert.DoesNotContain("y2", r.StdOut);
    }

    [WindowsFact]
    public async Task Kills_on_timeout()
    {
        var r = await _runner.RunAsync(new ProcessSpec("ping", ["-n", "20", "127.0.0.1"], Timeout: TimeSpan.FromMilliseconds(500)));
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task Missing_command_throws_FileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _runner.RunAsync(new ProcessSpec("definitely-not-a-command-xyz", [])));
    }

    [WindowsFact]
    public async Task Runs_cmd_scripts_with_spaced_arguments()
    {
        var dir = Directory.CreateTempSubdirectory("ghn proc ");
        var script = Path.Combine(dir.FullName, "echo arg.cmd");
        File.WriteAllText(script, "@echo [%~1]");
        var r = await _runner.RunAsync(new ProcessSpec(script, ["a b"]));
        Assert.Contains("[a b]", r.StdOut);
    }

    static string EchoScript()
    {
        var dir = Directory.CreateTempSubdirectory("ghn proc ");
        var script = Path.Combine(dir.FullName, "echo arg.cmd");
        // Delayed expansion so the script itself does not re-interpret & | in the argument.
        File.WriteAllText(script, "@setlocal enabledelayedexpansion\r\n@set \"x=%~1\"\r\n@echo [!x!]\r\n");
        return script;
    }

    [WindowsFact]
    public async Task Cmd_script_metacharacters_reach_script_intact()
    {
        var r = await _runner.RunAsync(new ProcessSpec(EchoScript(), ["a & b | c"]));
        Assert.Contains("[a & b | c]", r.StdOut);
    }

    [WindowsTheory]
    [InlineData("a\" & calc")]
    [InlineData("%PATH%")]
    public async Task Cmd_script_rejects_unsafe_arguments(string arg)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _runner.RunAsync(new ProcessSpec(EchoScript(), [arg])));
    }

    [WindowsFact]
    public async Task Passes_environment_variables()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd", ["/c", "echo %GHN_TEST%"],
            Env: new Dictionary<string, string> { ["GHN_TEST"] = "it-works" }));
        Assert.Contains("it-works", r.StdOut);
    }

    [UnixFact]
    public async Task Captures_stdout_exit_code_and_environment_through_sh()
    {
        var r = await _runner.RunAsync(new ProcessSpec("sh", ["-c", "echo hello \"$GHN_TEST\"; exit 3"],
            Env: new Dictionary<string, string> { ["GHN_TEST"] = "it-works" }));
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("hello it-works", r.StdOut);
    }

    [UnixFact]
    public async Task Writes_stdin_and_kills_on_timeout_through_sh()
    {
        var r = await _runner.RunAsync(new ProcessSpec("grep", ["x"], StdIn: "x1\ny2\n"));
        Assert.Contains("x1", r.StdOut);
        Assert.DoesNotContain("y2", r.StdOut);
        var slow = await _runner.RunAsync(new ProcessSpec("sleep", ["20"], Timeout: TimeSpan.FromMilliseconds(500)));
        Assert.True(slow.TimedOut);
    }
}
