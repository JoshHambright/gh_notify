using GhNotify.Core;

namespace GhNotify.Agents;

public interface IClaudeLauncher
{
    void Resume(string workingDir, string sessionId);
}

public sealed class ClaudeLauncher(IProcessRunner runner, Func<string, string?>? find = null) : IClaudeLauncher
{
    readonly Func<string, string?> _find = find ?? CommandResolver.Find;

    public void Resume(string workingDir, string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _)) throw new ArgumentException($"not a session id: {sessionId}", nameof(sessionId));
        // Full path so npm installs (claude.cmd) resolve: wt does not apply PATHEXT to .cmd shims.
        var claude = _find("claude") ?? "claude";
        if (_find("wt") is not null)
        {
            runner.StartDetached(new ProcessSpec("wt", ["-d", workingDir, claude, "--resume", sessionId]));
            return;
        }
        var quotedDir = workingDir.Replace("'", "''");
        var quotedClaude = claude.Replace("'", "''");
        runner.StartDetached(new ProcessSpec("powershell",
            ["-NoExit", "-Command", $"Set-Location -LiteralPath '{quotedDir}'; & '{quotedClaude}' --resume {sessionId}"],
            WorkingDirectory: workingDir));
    }
}
