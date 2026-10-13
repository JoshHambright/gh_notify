using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AdoClient(HttpClient http, ITokenProvider tokens, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    DateTimeOffset _blockedUntil;

    public Task<JsonElement> GetAsync(string absoluteUrl, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, absoluteUrl, null, ct);

    public async Task<JsonElement> SendAsync(HttpMethod method, string absoluteUrl, object? body, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (now < _blockedUntil) throw new RateLimitedException(_blockedUntil);
        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.GetTokenAsync(attempt > 0, ct);
            using var req = new HttpRequestMessage(method, absoluteUrl);
            if (body is not null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation)
            {
                if (attempt == 0) continue;
                throw new SourceAuthException("Azure DevOps rejected the az token — run `az login`");
            }
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _blockedUntil = now + (resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
                throw new RateLimitedException(_blockedUntil);
            }
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Azure DevOps {(int)resp.StatusCode} for {new Uri(absoluteUrl).AbsolutePath}: {(text.Length > 300 ? text[..300] + "…" : text)}",
                    null, resp.StatusCode);
            if (string.IsNullOrWhiteSpace(text)) return default;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }
}
