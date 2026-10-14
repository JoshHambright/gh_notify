using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

/// <param name="lastKnown">The reconciler's last-known items for this source; see <see cref="GitHubApprovals"/>.</param>
public sealed class GitHubSource(GitHubClient client, Func<GitHubConfig> config, Func<IReadOnlyList<InboxItem>>? lastKnown = null)
    : IInboxSource
{
    readonly GitHubApprovals _approvals = new(client, lastKnown: lastKnown);

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
            items.AddRange(GitHubPrMapper.MyPrs(data, cfg.IncludeDrafts));
            var extra = cfg.WatchRepos.Concat(items.Select(i => i.Repo)).Distinct(StringComparer.OrdinalIgnoreCase);
            items.AddRange(await _approvals.FetchAsync(extra, ct));
            return SourceResult.Ok(items);
        }
        catch (Exception ex) when (ex is SourceAuthException or RateLimitedException or HttpRequestException or JsonException)
        {
            return SourceResult.Fail(ex.Message);
        }
    }
}
