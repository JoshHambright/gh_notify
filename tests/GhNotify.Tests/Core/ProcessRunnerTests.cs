using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ProcessRunnerTests
{
    readonly ProcessRunner _runner = new();

    [Fact]
    public async Task Captures_stdout_and_exit_code()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd", ["/c", "echo hello"]));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello", r.StdOut);
    }

    [Fact]
    public async Task Writes_stdin()
    {
        var r = await _runner.RunAsync(new ProcessSpec("findstr", ["x"], StdIn: "x1\ny2\n"));
        Assert.Contains("x1", r.StdOut);
        Assert.DoesNotContain("y2", r.StdOut);
    }

    [Fact]
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

    [Fact]
    public async Task Runs_cmd_scripts_with_spaced_arguments()
    {
        var dir = Directory.CreateTempSubdirectory("ghn proc ");
        var script = Path.Combine(dir.FullName, "echo arg.cmd");
        File.WriteAllText(script, "@echo [%~1]");
        var r = await _runner.RunAsync(new ProcessSpec(script, ["a b"]));
        Assert.Contains("[a b]", r.StdOut);
    }

    [Fact]
    public async Task Passes_environment_variables()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd", ["/c", "echo %GHN_TEST%"],
            Env: new Dictionary<string, string> { ["GHN_TEST"] = "it-works" }));
        Assert.Contains("it-works", r.StdOut);
    }
}
