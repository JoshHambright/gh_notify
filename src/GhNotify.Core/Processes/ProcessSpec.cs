namespace GhNotify.Core;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Args,
    string? WorkingDirectory = null,
    string? StdIn = null,
    IReadOnlyDictionary<string, string>? Env = null,
    TimeSpan? Timeout = null);

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
    void StartDetached(ProcessSpec spec);
}
