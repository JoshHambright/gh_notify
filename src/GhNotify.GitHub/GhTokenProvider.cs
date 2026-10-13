using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GhTokenProvider(IProcessRunner runner, TimeProvider? time = null) : ITokenProvider
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly SemaphoreSlim _gate = new(1, 1);
    string? _token;
    DateTimeOffset _expires;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            if (!forceRefresh && _token is not null && now < _expires) return _token;
            ProcessResult r;
            try
            {
                r = await runner.RunAsync(new ProcessSpec("gh", ["auth", "token"], Timeout: TimeSpan.FromSeconds(15)), ct);
            }
            catch (FileNotFoundException)
            {
                throw new SourceAuthException("GitHub CLI (gh) not found — install it and run `gh auth login`");
            }
            var token = r.StdOut.Trim();
            if (r.ExitCode != 0 || token.Length == 0)
                throw new SourceAuthException("gh is not logged in — run `gh auth login`");
            _token = token;
            _expires = now.AddMinutes(30);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
