namespace GhNotify.GitHub;

public static class GitHubQueries
{
    public const string Inbox = """
        query($reviewQ: String!, $mineQ: String!) {
          viewer { login }
          review: search(query: $reviewQ, type: ISSUE, first: 50) { nodes { ...pr } }
          mine: search(query: $mineQ, type: ISSUE, first: 50) {
            nodes {
              ...pr
              ... on PullRequest {
                reviewDecision
                commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
                reviewThreads(first: 50) { nodes { isResolved comments(last: 1) { nodes { author { login } } } } }
              }
            }
          }
        }
        fragment pr on PullRequest {
          number title url isDraft updatedAt additions deletions headRefOid headRefName baseRefName createdAt
          author { __typename login }
          repository { nameWithOwner url }
        }
        """;

    public const string MineQuery = "is:pr is:open archived:false author:@me";

    public static string ReviewQuery(bool includeDrafts) =>
        "is:pr is:open archived:false review-requested:@me" + (includeDrafts ? "" : " draft:false");
}
