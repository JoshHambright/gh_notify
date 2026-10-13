using System.Diagnostics;
using System.Text;

namespace GhNotify.Core;

public sealed class ProcessRunner : IProcessRunner
{
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        var psi = CommandResolver.CreateStartInfo(spec.FileName, spec.Args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = spec.StdIn is not null;
        psi.StandardOutputEncoding = Utf8;
        psi.StandardErrorEncoding = Utf8;
        if (spec.StdIn is not null) psi.StandardInputEncoding = Utf8;
        if (spec.WorkingDirectory is not null) psi.WorkingDirectory = spec.WorkingDirectory;
        if (spec.Env is not null)
            foreach (var (k, v) in spec.Env) psi.Environment[k] = v;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {spec.FileName}");
        var stdout = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = p.StandardError.ReadToEndAsync(CancellationToken.None);
        if (spec.StdIn is not null)
        {
            try
            {
                await p.StandardInput.WriteAsync(spec.StdIn);
                p.StandardInput.Close();
            }
            catch (IOException) { /* process exited without reading stdin */ }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (spec.Timeout is { } t) cts.CancelAfter(t);
        var timedOut = false;
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await p.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            timedOut = true;
        }
        return new ProcessResult(timedOut ? -1 : p.ExitCode, await stdout, await stderr, timedOut);
    }

    public void StartDetached(ProcessSpec spec)
    {
        var psi = CommandResolver.CreateStartInfo(spec.FileName, spec.Args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = false;
        if (spec.WorkingDirectory is not null) psi.WorkingDirectory = spec.WorkingDirectory;
        Process.Start(psi)?.Dispose();
    }
}
