using GhNotify.Core;

namespace GhNotify.Tests.Support;

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessSpec> Calls { get; } = [];
    public List<ProcessSpec> Detached { get; } = [];
    public Func<ProcessSpec, ProcessResult> Handler { get; set; } = _ => Ok();

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        lock (Calls)
            Calls.Add(spec);
        return Task.FromResult(Handler(spec));
    }

    public void StartDetached(ProcessSpec spec) => Detached.Add(spec);

    public static ProcessResult Ok(string stdout = "") => new(0, stdout, "", false);
    public static ProcessResult Fail(string stderr, int code = 1) => new(code, "", stderr, false);
}
