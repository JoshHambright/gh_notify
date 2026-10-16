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
        runner.StartDetached(TerminalCommand.Build(setting, workingDir, command));
    }
}
