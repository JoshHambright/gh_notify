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
        // --setting-sources user: a PR worktree's own .claude/settings.json (hooks, permissions) must not apply.
        Assert.Equal(["-p", "--output-format", "json", "--permission-mode", "default", "--setting-sources", "user"], args.Take(7));
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

    public static readonly TheoryData<string> ExpectedDenied = new(
    [
        // Original list
        "Bash(gh pr review:*)", "Bash(gh pr comment:*)", "Bash(gh pr merge:*)", "Bash(gh pr close:*)", "Bash(gh pr edit:*)",
        "Bash(gh pr ready:*)", "Bash(gh issue comment:*)",
        "Bash(gh api -X POST:*)", "Bash(gh api -X PATCH:*)", "Bash(gh api -X PUT:*)", "Bash(gh api -X DELETE:*)",
        "Bash(gh api --method POST:*)", "Bash(gh api --method PATCH:*)", "Bash(gh api --method PUT:*)", "Bash(gh api --method DELETE:*)",
        "Bash(git push:*)", "Bash(git commit:*)",
        "Bash(az repos pr create:*)", "Bash(az repos pr update:*)", "Bash(az repos pr set-vote:*)", "Bash(az repos pr reviewer:*)",
        "Bash(az repos pr work-item:*)", "Bash(az pipelines run:*)", "Bash(az rest:*)",
        // Task 12
        "Bash(gh api * -f *)", "Bash(gh api * -F *)", "Bash(gh api * --field *)", "Bash(gh api * --raw-field *)", "Bash(gh api * --input *)",
        "Bash(gh pr * --body *)",
        "Bash(gh pr create:*)", "Bash(gh issue create:*)", "Bash(gh release create:*)", "Bash(gh api -f:*)", "Bash(gh api -F:*)",
        "Bash(gh api -X*)", "Bash(gh api --method=*)",
        // Final review: method flags after the path, git global options before the verb, az write verbs
        "Bash(gh api * -X *)", "Bash(gh api * -X*)", "Bash(gh api * --method *)", "Bash(gh api * --method=*)",
        "Bash(git * push*)", "Bash(git * commit*)",
        "Bash(az repos ref:*)", "Bash(az repos delete:*)", "Bash(az repos update:*)", "Bash(az repos create:*)", "Bash(az repos import:*)",
        "Bash(az repos policy:*)", "Bash(az repos pr policy queue:*)",
        "Bash(az pipelines build queue:*)", "Bash(az pipelines release create:*)", "Bash(az pipelines variable:*)",
        "Bash(az pipelines variable-group:*)", "Bash(az pipelines delete:*)", "Bash(az pipelines create:*)", "Bash(az pipelines update:*)",
        "Bash(az devops invoke:*)",
    ]);

    [Theory]
    [MemberData(nameof(ExpectedDenied))]
    public void Global_deny_list_contains(string entry)
    {
        Assert.Contains(entry, AgentSafety.DisallowedTools);
        Assert.Contains(entry, ClaudeRunner.BuildArgs(new AgentProfile(), []));
    }

    [Fact]
    public void Default_allow_list_has_only_read_verbs_for_az()
    {
        var allowed = new AppConfig().AgentAllowedTools;
        Assert.DoesNotContain("Bash(az repos:*)", allowed);
        Assert.DoesNotContain("Bash(az pipelines:*)", allowed);
        Assert.Equal(
            ["Bash(az repos pr show:*)", "Bash(az repos pr list:*)", "Bash(az repos pr policy list:*)", "Bash(az pipelines runs show:*)",
             "Bash(az pipelines runs list:*)", "Bash(az pipelines build show:*)", "Bash(az pipelines build list:*)"],
            allowed.Where(a => a.StartsWith("Bash(az ", StringComparison.Ordinal)));
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
