using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubClient
{
    static readonly Uri Api = new("https://api.github.com/");
    readonly HttpClient _http;
    readonly ITokenProvider _tokens;
    readonly TimeProvider _time;
    DateTimeOffset _blockedUntil;

    public GitHubClient(HttpClient http, ITokenProvider tokens, TimeProvider? time = null)
    {
        _http = http;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
    }

    public async Task<JsonElement> GraphQLAsync(string query, object variables, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { query, variables });
        var root = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, new Uri(Api, "graphql"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }, ct);
        // Partial errors (data present but some fields failed) are treated as failures too: a field that errored comes back null,
        // and mapping it as "no items" would remove items from a source that only looks healthy.
        var errors = root.Arr("errors").ToList();
        if (errors.Count > 0 || root.Obj("data").ValueKind != JsonValueKind.Object)
        {
            var message = errors.Select(e => e.Str("message")).FirstOrDefault(m => m is not null) ?? (errors.Count > 0 ? "error" : "no data");
            throw new HttpRequestException($"GitHub GraphQL: {message}");
        }
        return root.GetProperty("data");
    }

    public Task<JsonElement> GetAsync(string relativePath, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, new Uri(Api, relativePath)), ct);

    public Task<JsonElement> PostAsync(string relativePath, object body, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, new Uri(Api, relativePath))
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") }, ct);

    async Task<JsonElement> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (now < _blockedUntil) throw new RateLimitedException(_blockedUntil);
        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokens.GetTokenAsync(attempt > 0, ct);
            using var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd("gh-notify/1.0");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (attempt == 0) continue;
                throw new SourceAuthException("GitHub rejected the gh token — run `gh auth login`");
            }
            if (IsRateLimited(resp))
            {
                _blockedUntil = RetryAt(resp, now);
                throw new RateLimitedException(_blockedUntil);
            }
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"GitHub {(int)resp.StatusCode} for {req.RequestUri!.AbsolutePath}: {Truncate(text)}", null, resp.StatusCode);
            if (string.IsNullOrWhiteSpace(text)) return default;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }

    static bool IsRateLimited(HttpResponseMessage resp) =>
        resp.StatusCode == HttpStatusCode.TooManyRequests ||
        (resp.StatusCode == HttpStatusCode.Forbidden &&
         (resp.Headers.RetryAfter is not null ||
          (resp.Headers.TryGetValues("x-ratelimit-remaining", out var v) && v.FirstOrDefault() == "0")));

    static DateTimeOffset RetryAt(HttpResponseMessage resp, DateTimeOffset now)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta) return now + delta;
        if (resp.Headers.TryGetValues("x-ratelimit-reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        return now.AddMinutes(5);
    }

    static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
