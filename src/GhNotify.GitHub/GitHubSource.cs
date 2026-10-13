using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubSource(GitHubClient client, Func<GitHubConfig> config) : IInboxSource
{
    public string Name => "GitHub";

    public async Task<SourceResult> FetchAsync(CancellationToken ct)
    {
        try
        {
            var cfg = config();
            var data = await client.GraphQLAsync(GitHubQueries.Inbox,
                new { reviewQ = GitHubQueries.ReviewQuery(cfg.IncludeDrafts), mineQ = GitHubQueries.MineQuery }, ct);
            var items = new List<InboxItem>();
            items.AddRange(GitHubPrMapper.ReviewRequests(data));
            items.AddRange(GitHubPrMapper.MyPrs(data));
            return SourceResult.Ok(items);
        }
        catch (Exception ex) when (ex is SourceAuthException or RateLimitedException or HttpRequestException or JsonException)
        {
            return SourceResult.Fail(ex.Message);
        }
    }
}
