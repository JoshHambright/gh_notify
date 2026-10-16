using GhNotify.Core;

namespace GhNotify.Platform.Mac;

/// <summary>
/// Builds the process that opens a terminal in a folder running a command (display.terminal): Terminal.app or iTerm2 through
/// osascript, or a custom command that gets the folder and the command line as its two arguments. osascript and the custom command
/// get their arguments as an argv array, never through a shell; the only shell is the terminal's own, which runs the
/// <see cref="ScriptLine"/> with every token single-quoted.
/// </summary>
public static class TerminalCommand
{
    public static ProcessSpec Build(string terminal, string workingDir, string command)
    {
        var line = ScriptLine(workingDir, command);
        return terminal.Trim().ToLowerInvariant() switch
        {
            "" or DisplayConfig.DefaultTerminal => new ProcessSpec("osascript",
            [
                "-e", $"tell application \"Terminal\" to do script \"{AppleScriptEscape(line)}\"",
                "-e", "tell application \"Terminal\" to activate",
            ]),
            "iterm" => new ProcessSpec("osascript",
            [
                "-e", "tell application \"iTerm\"",
                "-e", "activate",
                "-e", "set w to (create window with default profile)",
                "-e", $"tell current session of w to write text \"{AppleScriptEscape(line)}\"",
                "-e", "end tell",
            ]),
            _ => new ProcessSpec(terminal.Trim(), [workingDir, command], WorkingDirectory: workingDir),
        };
    }

    /// <summary><c>cd '&lt;dir&gt;' &amp;&amp; '&lt;tok1&gt;' '&lt;tok2&gt;'…</c>, the command split like Windows does (<see cref="CommandLine.Split"/>).</summary>
    public static string ScriptLine(string workingDir, string command)
    {
        var tokens = CommandLine.Split(command);
        var cd = "cd " + ShellQuote(workingDir);
        return tokens.Count == 0 ? cd : cd + " && " + string.Join(' ', tokens.Select(ShellQuote));
    }

    /// <summary>POSIX single quotes: nothing inside is special; an embedded ' becomes '\''.</summary>
    public static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Inside an AppleScript string literal only \ and " are special.</summary>
    public static string AppleScriptEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
