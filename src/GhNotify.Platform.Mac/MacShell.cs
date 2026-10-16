using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary><c>open</c> for URLs and folders; terminals per display.terminal (<see cref="TerminalCommand"/>).</summary>
public sealed class MacShell(IProcessRunner runner, Func<string> terminal) : IShell
{
    public void OpenUrl(string httpsUrl)
    {
        if (!ItemUrl.IsOpenable(httpsUrl))
        {
            Log.Error($"refusing to open non-https item URL: {httpsUrl}");
            return;
        }
        runner.StartDetached(new ProcessSpec("open", [httpsUrl]));
    }

    /// <summary>Opens a folder in Finder, or a file (config.json) in its default app.</summary>
    public void OpenFolder(string path) => runner.StartDetached(new ProcessSpec("open", [path]));

    public void OpenTerminal(string workingDir, string command)
    {
        string setting;
        try { setting = terminal(); }
        catch (Exception ex)
        {
            Log.Error("could not read display.terminal; using Terminal.app", ex);
            setting = DisplayConfig.DefaultTerminal;
        }
        var spec = TerminalCommand.Build(setting, workingDir, command);
        if (spec.FileName != "osascript")
        {
            runner.StartDetached(spec); // a custom terminal command: it may keep running
            return;
        }
        // osascript returns as soon as the terminal has the script; run it so a refusal (no Automation permission, iTerm missing)
        // is logged instead of the click silently doing nothing.
        runner.RunAsync(spec with { Timeout = TimeSpan.FromSeconds(30) }).ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Error("could not open a terminal", t.Exception!.GetBaseException());
            else if (t.Result.ExitCode != 0) Log.Error($"could not open a terminal: {TerminalError(t.Result.StdErr)}");
        }, TaskScheduler.Default);
    }

    /// <summary>osascript's message, plus what to do about the common case (error -1743: not allowed to control the terminal).</summary>
    internal static string TerminalError(string stderr)
    {
        var message = stderr.Trim();
        return message.Contains("-1743")
            ? message + " — allow gh_notify in System Settings → Privacy & Security → Automation"
            : message.Length > 0 ? message : "osascript failed";
    }
}
