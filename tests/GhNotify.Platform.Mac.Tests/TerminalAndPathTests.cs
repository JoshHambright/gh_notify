namespace GhNotify.Platform.Mac.Tests;

public class TerminalAndPathTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("with space", "'with space'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$HOME `x` \\n \"q\"", "'$HOME `x` \\n \"q\"'")]
    [InlineData("", "''")]
    public void ShellQuote_makes_every_character_literal(string input, string quoted) =>
        Assert.Equal(quoted, TerminalCommand.ShellQuote(input));

    [Fact]
    public void Script_line_cds_then_runs_each_token_quoted()
    {
        Assert.Equal("cd '/Users/alice/dev/acme api' && 'claude' '--resume' 'a b'",
            TerminalCommand.ScriptLine("/Users/alice/dev/acme api", "claude --resume \"a b\""));
        Assert.Equal("cd '/tmp'", TerminalCommand.ScriptLine("/tmp", ""));
        // Tokens are split on spaces like Windows, and each one is literal: the substitution never runs.
        Assert.Equal("cd '/Users/alice/o'\\''brien' && 'echo' '$(rm' '-rf' '~)'",
            TerminalCommand.ScriptLine("/Users/alice/o'brien", "echo $(rm -rf ~)"));
    }

    [Fact]
    public void Terminal_app_gets_the_line_inside_an_escaped_AppleScript_string()
    {
        var spec = TerminalCommand.Build("terminal", "/Users/alice/dev/back\\slash", "echo \"hi\"");
        Assert.Equal("osascript", spec.FileName);
        Assert.Equal("-e", spec.Args[0]);
        // ScriptLine: cd '/Users/alice/dev/back\slash' && 'echo' 'hi'  (the splitter removes the double quotes)
        Assert.Equal("tell application \"Terminal\" to do script \"cd '/Users/alice/dev/back\\\\slash' && 'echo' 'hi'\"", spec.Args[1]);
        Assert.Equal("tell application \"Terminal\" to activate", spec.Args[3]);
    }

    [Fact]
    public void A_double_quote_inside_a_token_is_escaped_for_AppleScript() =>
        Assert.Equal("say \\\"x\\\" \\\\ y", TerminalCommand.AppleScriptEscape("say \"x\" \\ y"));

    [Fact]
    public void iTerm_writes_the_line_into_a_new_window()
    {
        var spec = TerminalCommand.Build(" iTerm ", "/tmp", "ls");
        Assert.Equal("osascript", spec.FileName);
        Assert.Contains("tell application \"iTerm\"", spec.Args);
        Assert.Contains("tell current session of w to write text \"cd '/tmp' && 'ls'\"", spec.Args);
    }

    [Fact]
    public void A_custom_terminal_gets_the_folder_and_the_command_as_two_arguments()
    {
        var spec = TerminalCommand.Build("/usr/local/bin/wezterm-open", "/Users/alice/dev", "claude --resume abc");
        Assert.Equal("/usr/local/bin/wezterm-open", spec.FileName);
        Assert.Equal(["/Users/alice/dev", "claude --resume abc"], spec.Args);
        Assert.Equal("/Users/alice/dev", spec.WorkingDirectory);
    }

    [Fact]
    public void Path_merge_appends_new_shell_entries_then_homebrew_without_duplicates()
    {
        var merged = LoginShellPath.Combine("/usr/bin:/bin", "/opt/homebrew/bin:/usr/bin::relative:/Users/alice/.local/bin",
            dir => dir is "/opt/homebrew/bin" or "/usr/local/bin");
        Assert.Equal("/usr/bin:/bin:/opt/homebrew/bin:/Users/alice/.local/bin:/usr/local/bin", merged);
        Assert.Equal("/usr/bin", LoginShellPath.Combine("/usr/bin", null, _ => false));
    }

    [Fact]
    public void A_noisy_profile_does_not_end_up_in_PATH()
    {
        Assert.Equal("/usr/bin:/opt/homebrew/bin", LoginShellPath.LastLine("Welcome back!\nnvm: using node 22\n/usr/bin:/opt/homebrew/bin"));
        Assert.Equal("/usr/bin", LoginShellPath.LastLine("/usr/bin\n"));
    }

    [Fact]
    public void Merge_keeps_PATH_when_the_shell_times_out()
    {
        var before = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/usr/bin:/bin");
            var runner = new FakeRunner { Result = _ => new Core.ProcessResult(-1, "/evil", "", TimedOut: true) };
            LoginShellPath.Merge(runner);
            Assert.DoesNotContain("/evil", Environment.GetEnvironmentVariable("PATH"));
            Assert.StartsWith("/usr/bin:/bin", Environment.GetEnvironmentVariable("PATH"));

            runner.Result = spec => new Core.ProcessResult(0, "/Users/alice/bin", "", false);
            LoginShellPath.Merge(runner);
            Assert.Contains("/Users/alice/bin", Environment.GetEnvironmentVariable("PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", before);
        }
    }
}
