using GhNotify.Agents;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Agents;

public class ClaudeRunnerTests
{
    [Fact]
    public void Args_merge_allowed_and_always_include_global_deny_list()
    {
        var profile = new AgentProfile { AllowedTools = ["Bash(az:*)", "Read"], DisallowedTools = ["Bash(rm:*)"] };
        var args = ClaudeRunner.BuildArgs(profile, ["Read", "Grep"]);
        Assert.Equal(["-p", "--output-format", "json", "--permission-mode", "default"], args.Take(5));
        var allowStart = args.ToList().IndexOf("--allowedTools");
        var denyStart = args.ToList().IndexOf("--disallowedTools");
        Assert.Equal(["Read", "Grep", "Bash(az:*)"], args.Skip(allowStart + 1).Take(denyStart - allowStart - 1));
        var denied = args.Skip(denyStart + 1).ToList();
        Assert.Contains("Bash(git push:*)", denied);
        Assert.Contains("Bash(gh pr review:*)", denied);
        Assert.Contains("Bash(gh api * -f *)", denied);
        Assert.Contains("Bash(gh pr create:*)", denied);
        Assert.Contains("Bash(gh api -X*)", denied);
        Assert.Contains("Bash(rm:*)", denied);
        Assert.Equal(AgentSafety.DisallowedTools.Count + 1, denied.Count);
    }

    [Fact]
    public void Args_omit_allowed_flag_when_nothing_allowed()
    {
        var args = ClaudeRunner.BuildArgs(new AgentProfile(), []);
        Assert.DoesNotContain("--allowedTools", args);
        Assert.Contains("--disallowedTools", args);
    }

    [Fact]
    public async Task Sends_prompt_on_stdin_in_working_dir_with_timeout()
    {
        var runner = new FakeProcessRunner
        {
            Handler = _ => FakeProcessRunner.Ok("""{"type":"result","is_error":false,"result":"done\nSUMMARY: 2 findings","session_id":"s-1","total_cost_usd":0.37}"""),
        };
        var outcome = await new ClaudeRunner(runner).RunAsync(@"C:\wt", "/peer-review https://x", new AgentProfile(), [], TimeSpan.FromMinutes(3), default);
        var call = runner.Calls.Single();
        Assert.Equal("claude", call.FileName);
        Assert.Equal("/peer-review https://x", call.StdIn);
        Assert.Equal(@"C:\wt", call.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(3), call.Timeout);
        Assert.DoesNotContain(call.Args, a => a.Contains("peer-review"));
        Assert.Equal(new AgentOutcome(true, "s-1", "2 findings", 0.37m, null), outcome);
    }

    [Fact]
    public void Error_result_is_failure_with_session_kept()
    {
        var o = ClaudeRunner.Parse(new ProcessResult(1, """{"is_error":true,"result":"Skill not found","session_id":"s-2"}""", "", false));
        Assert.False(o.Success);
        Assert.Equal("s-2", o.SessionId);
        Assert.Equal("Skill not found", o.Error);
    }

    [Fact]
    public void Timeout_is_failure() =>
        Assert.Equal("timed out", ClaudeRunner.Parse(new ProcessResult(-1, "", "", true)).Error);

    [Fact]
    public void Non_json_output_is_failure_with_stderr_tail() =>
        Assert.Equal("boom", ClaudeRunner.Parse(new ProcessResult(2, "not json", "boom", false)).Error);

    [Theory]
    [InlineData("a\nSUMMARY: two issues", "two issues")]
    [InlineData("a\n**SUMMARY:** two issues", "two issues")]
    [InlineData("a\n  summary:   lower  ", "lower")]
    [InlineData("first\nlast line\n\n", "last line")]
    [InlineData(null, null)]
    public void Extracts_summary(string? result, string? expected) =>
        Assert.Equal(expected, ClaudeRunner.ExtractSummary(result));
}
