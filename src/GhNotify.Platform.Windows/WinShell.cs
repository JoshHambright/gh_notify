using System.Diagnostics;
using System.Text;
using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Windows;

public sealed class WinShell(IProcessRunner runner, Func<string, string?>? find = null) : IShell
{
    readonly Func<string, string?> _find = find ?? CommandResolver.Find;

    public void OpenUrl(string httpsUrl)
    {
        if (!ItemUrl.IsOpenable(httpsUrl))
        {
            Log.Error($"refusing to open non-https item URL: {httpsUrl}");
            return;
        }
        ShellExecute(httpsUrl);
    }

    /// <summary>ShellExecute: opens a folder in Explorer, or a file (config.json) in its default editor.</summary>
    public void OpenFolder(string path) => ShellExecute(path);

    static void ShellExecute(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    /// <summary>Windows Terminal when installed (<c>wt -d dir command...</c>), otherwise a PowerShell window.</summary>
    public void OpenTerminal(string workingDir, string command)
    {
        var tokens = SplitCommand(command);
        if (_find("wt") is not null)
        {
            runner.StartDetached(new ProcessSpec("wt", ["-d", workingDir, .. tokens]));
            return;
        }
        var quotedDir = workingDir.Replace("'", "''");
        var invocation = tokens.Count == 0
            ? ""
            : "& " + string.Join(' ', tokens.Select((t, i) => i == 0 ? "'" + t.Replace("'", "''") + "'" : t));
        runner.StartDetached(new ProcessSpec("powershell",
            ["-NoExit", "-Command", $"Set-Location -LiteralPath '{quotedDir}'; {invocation}"],
            WorkingDirectory: workingDir));
    }

    /// <summary>Splits a command line on spaces; a "double quoted" section is one token (quotes removed).</summary>
    internal static List<string> SplitCommand(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in command)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (c == ' ' && !quoted)
            {
                if (any) tokens.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else { current.Append(c); any = true; }
        }
        if (any) tokens.Add(current.ToString());
        return tokens;
    }
}
