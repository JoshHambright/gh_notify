using System.Globalization;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AzTokenProvider(IProcessRunner runner, TimeProvider? time = null) : ITokenProvider
{
    public const string AdoResource = "499b84ac-1321-427f-aa17-267ca6b56798";
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly SemaphoreSlim _gate = new(1, 1);
    string? _token;
    DateTimeOffset _expires;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null && _time.GetUtcNow() < _expires.AddMinutes(-5)) return _token;
            ProcessResult r;
            try
            {
                r = await runner.RunAsync(new ProcessSpec("az",
                    ["account", "get-access-token", "--resource", AdoResource, "--output", "json"], Timeout: TimeSpan.FromSeconds(30)), ct);
            }
            catch (FileNotFoundException)
            {
                throw new SourceAuthException("Azure CLI (az) not found — install it and run `az login`");
            }
            if (r.ExitCode != 0 && r.StdErr.Contains("AADSTS", StringComparison.Ordinal))
                throw new SourceAuthException(
                    "az can't get an Azure DevOps token for the current tenant — run `az login --tenant <your ADO tenant> --scope 499b84ac-1321-427f-aa17-267ca6b56798/.default`");
            if (r.ExitCode != 0 || r.TimedOut)
                throw new SourceAuthException("az is not logged in or the token expired — run `az login`");
            using var doc = JsonDocument.Parse(r.StdOut);
            var root = doc.RootElement;
            _token = root.Str("accessToken") ?? throw new SourceAuthException("az returned no access token — run `az login`");
            _expires = ParseExpiry(root);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    static DateTimeOffset ParseExpiry(JsonElement root)
    {
        if (root.Long("expires_on") is { } epoch) return DateTimeOffset.FromUnixTimeSeconds(epoch);
        if (long.TryParse(root.Str("expires_on"), out var epochStr)) return DateTimeOffset.FromUnixTimeSeconds(epochStr);
        if (DateTime.TryParse(root.Str("expiresOn"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            return new DateTimeOffset(local);
        return DateTimeOffset.UtcNow.AddMinutes(10);
    }
}
