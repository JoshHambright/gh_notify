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
        if (_find("wt") is not null)
        {
            runner.StartDetached(new ProcessSpec("wt", ["-d", workingDir, "claude", "--resume", sessionId]));
            return;
        }
        var quoted = workingDir.Replace("'", "''");
        runner.StartDetached(new ProcessSpec("powershell",
            ["-NoExit", "-Command", $"Set-Location -LiteralPath '{quoted}'; claude --resume {sessionId}"], WorkingDirectory: workingDir));
    }
}
