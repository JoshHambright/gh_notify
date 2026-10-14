using GhNotify.Agents;
using GhNotify.Core;

namespace GhNotify.Tests.Agents;

public class CommandRunnerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ghn-cmd-" + Guid.NewGuid().ToString("N"));
    public CommandRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    static InboxItem It(string title = "Fix it") =>
        new("gh:review:acme/api#7", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", title, "https://github.com/acme/api/pull/7", "alice",
            DateTimeOffset.UnixEpoch, "abc", 3, null)
        { Number = 7 };

    sealed class Capture(ProcessResult result) : IProcessRunner
    {
        public ProcessSpec? Spec;
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default) { Spec = spec; return Task.FromResult(result); }
        public void StartDetached(ProcessSpec spec) { }
    }

    [Fact]
    public async Task Fills_placeholders_per_argument_and_sets_env()
    {
        var cap = new Capture(new ProcessResult(0, "ok\nSUMMARY: all good\nmore", "", false));
        var o = await new CommandRunner(cap).RunAsync(_dir, ["triage", "--pr", "{url}", "{number}"], It(), TimeSpan.FromMinutes(1), default);
        Assert.Equal("triage", cap.Spec!.FileName);
        Assert.Equal(["--pr", "https://github.com/acme/api/pull/7", "7"], cap.Spec.Args);
        Assert.Equal(_dir, cap.Spec.WorkingDirectory);
        Assert.Equal("acme/api", cap.Spec.Env!["GHN_REPO"]);
        Assert.Equal("ReviewRequested", cap.Spec.Env["GHN_KIND"]);
        Assert.Equal("gh:review:acme/api#7", cap.Spec.Env["GHN_ITEM_KEY"]);
        Assert.True(o.Success);
        Assert.Equal("all good", o.Summary);
    }

    [Theory]
    [InlineData(0, "line1\n\nlast line\n", true, "last line", null)]
    [InlineData(3, "boom", false, "boom", "exit 3")]
    public void Parse_exit_code_and_summary(int code, string stdout, bool ok, string summary, string? error)
    {
        var o = CommandRunner.Parse(new ProcessResult(code, stdout, "", false));
        Assert.Equal(ok, o.Success);
        Assert.Equal(summary, o.Summary);
        Assert.Equal(error, o.Error);
        Assert.Null(o.SessionId);
        Assert.Null(o.CostUsd);
    }

    [Fact]
    public void Parse_keeps_the_last_64KB()
    {
        var big = new string('x', 100_000) + "\nTAIL";
        var o = CommandRunner.Parse(new ProcessResult(0, big, "", false));
        Assert.Equal(CommandRunner.MaxOutput, o.ResultText!.Length);
        Assert.EndsWith("TAIL", o.ResultText);
    }

    [Fact]
    public void Parse_timeout() => Assert.Equal("timed out", CommandRunner.Parse(new ProcessResult(-1, "", "", true)).Error);

    [Fact]
    public async Task Title_with_metacharacters_is_one_argument()
    {
        var script = Path.Combine(_dir, "args.ps1");
        File.WriteAllText(script, "[Console]::Out.Write(\"$($args.Count)|$($args[0])\")");
        var title = "a \"b\" & c %PATH% | d";
        var o = await new CommandRunner(new ProcessRunner()).RunAsync(_dir, ["pwsh", "-NoProfile", "-File", script, "{title}"], It(title),
            TimeSpan.FromMinutes(1), default);
        Assert.True(o.Success, o.Error);
        Assert.Equal($"1|{title}", o.ResultText!.Trim());
    }

    [Fact]
    public async Task Cmd_script_with_quote_in_arg_fails_cleanly()
    {
        var marker = Path.Combine(_dir, "ran.txt");
        var cmd = Path.Combine(_dir, "t.cmd");
        File.WriteAllText(cmd, $"@echo ran> \"{marker}\"\r\n");
        var o = await new CommandRunner(new ProcessRunner()).RunAsync(_dir, [cmd, "{title}"], It("say \"hi\""), TimeSpan.FromMinutes(1), default);
        Assert.False(o.Success);
        Assert.Contains("cannot be passed safely", o.Error);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Missing_program_fails_with_message()
    {
        var o = await new CommandRunner(new ProcessRunner()).RunAsync(_dir, ["ghn-no-such-tool-xyz"], It(), TimeSpan.FromMinutes(1), default);
        Assert.False(o.Success);
        Assert.Contains("not found", o.Error);
    }
}
