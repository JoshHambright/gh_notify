using System.Net;
using System.Text;

namespace GhNotify.Tests.Support;

/// <summary>Routes requests by AbsoluteUri substring; first registered match wins; unmatched → 404.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    public FakeHttpHandler On(string urlContains, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(r => r.RequestUri!.AbsoluteUri.Contains(urlContains, StringComparison.Ordinal), _ => Respond(status, json));

    public FakeHttpHandler On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests)
            Requests.Add(request);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (Bodies)
            Bodies.Add(body);
        foreach (var (match, respond) in _routes)
            if (match(request)) return respond(request);
        return Respond(HttpStatusCode.NotFound, """{"message":"no fake route"}""");
    }
}
