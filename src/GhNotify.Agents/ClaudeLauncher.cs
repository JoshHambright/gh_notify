using GhNotify.Core;

namespace GhNotify.Agents;

public interface IClaudeLauncher
{
    void Resume(string workingDir, string sessionId);
}

/// <summary>Opens a terminal window in <paramref name="workingDir"/> running <paramref name="command"/> (a command line, args separated by spaces, quoted with " when needed).</summary>
public delegate void OpenTerminal(string workingDir, string command);

/// <summary>Resumes a claude session in a terminal. How the terminal is opened is the platform's business (IShell.OpenTerminal, injected).</summary>
public sealed class ClaudeLauncher(OpenTerminal openTerminal, Func<string, string?>? find = null) : IClaudeLauncher
{
    readonly Func<string, string?> _find = find ?? CommandResolver.Find;

    public void Resume(string workingDir, string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _)) throw new ArgumentException($"not a session id: {sessionId}", nameof(sessionId));
        // Full path so npm installs (claude.cmd) resolve: wt does not apply PATHEXT to .cmd shims.
        var claude = _find("claude") ?? "claude";
        openTerminal(workingDir, $"{Quote(claude)} --resume {sessionId}");
    }

    static string Quote(string s) => s.Contains(' ') || s.Contains('"') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
}
