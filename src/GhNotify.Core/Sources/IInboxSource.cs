namespace GhNotify.Core;

public interface IInboxSource
{
    string Name { get; }
    Task<SourceResult> FetchAsync(CancellationToken ct);
}

public sealed record SourceResult(bool Healthy, IReadOnlyList<InboxItem> Items, string? Problem)
{
    public static SourceResult Ok(IReadOnlyList<InboxItem> items) => new(true, items, null);
    public static SourceResult Fail(string problem) => new(false, [], problem);
}

public interface ITokenProvider
{
    Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct);
}

public sealed class SourceAuthException(string message) : Exception(message);

public sealed class RateLimitedException(DateTimeOffset until)
    : Exception($"rate limited until {until.ToLocalTime():HH:mm}")
{
    public DateTimeOffset Until { get; } = until;
}
