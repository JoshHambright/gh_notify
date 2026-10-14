using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class InboxFilterTests
{
    static InboxRow Row(string key, string repo, string title, string author, int? number, string? display = null, bool stale = false,
        params ItemAction[] actions) =>
        new(key, "GH", repo, title, "https://u/" + key, "1h", stale, null, null, null, null, false, null, false, false, [], null, null,
            actions.Select(a => new RowActionButton(a, a.ToString(), true, null)).ToList(), RowMode.Collapsed, null, null, false, null, null,
            display ?? repo)
        { Number = number is { } n ? $"#{n}" : null, Author = author };

    static readonly InboxRow Api12 = Row("a", "contoso/api", "Fix retry loop", "alice", 12, "api");
    static readonly InboxRow Web7 = Row("b", "fabrikam/web", "Bump deps", "bob", 7);
    static readonly InboxRow Api120 = Row("c", "contoso/api", "Docs", "bob", 120, "api");

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("retry", true)]
    [InlineData("RETRY", true)]
    [InlineData("api", true)]
    [InlineData("alice", true)]
    [InlineData("#12", true)]
    [InlineData("12", true)]
    [InlineData("#1", false)]
    [InlineData("1", false)]
    [InlineData("api retry", true)]
    [InlineData("  api   alice ", true)]
    [InlineData("api bob", false)]
    [InlineData("zzz", false)]
    public void Matches_terms(string query, bool expected) => Assert.Equal(expected, InboxFilter.Matches(Api12, query));

    [Fact]
    public void Null_query_matches() => Assert.True(InboxFilter.Matches(Api12, null));

    [Fact]
    public void Matches_repo_alias_and_original_name()
    {
        Assert.True(InboxFilter.Matches(Api12, "api"));
        Assert.True(InboxFilter.Matches(Api12, "contoso"));
        var aliased = Row("d", "fabrikam/very-long-repository", "T", "bob", 3, "fabrikam/very-lo…");
        Assert.True(InboxFilter.Matches(aliased, "very-lo…"));
        Assert.True(InboxFilter.Matches(aliased, "long-repository"));
    }

    [Fact]
    public void Number_must_match_exactly_not_by_prefix() =>
        Assert.True(InboxFilter.Matches(Api120, "#120") && !InboxFilter.Matches(Api120, "#12"));

    [Fact]
    public void Apply_filters_drops_empty_sections_and_recounts()
    {
        var sections = new List<InboxSection>
        {
            new("Review requests", [Api12, Web7, Api120]) { Id = "ReviewRequested" },
            new("Approvals", [Web7]) { Id = "ApprovalPending" },
        };
        var result = InboxFilter.Apply(sections, "api");
        var only = Assert.Single(result);
        Assert.Equal("ReviewRequested", only.Id);
        Assert.Equal(["a", "c"], only.Rows.Select(r => r.Key));
        Assert.Equal(2, only.Count);
    }

    [Fact]
    public void Apply_expands_collapsible_sections_and_uses_filtered_counts()
    {
        var waiting = new InboxSection("Waiting on others", [Api12, Web7]) { Id = "waiting", Collapsible = true, Expanded = false, Count = 9 };
        var result = InboxFilter.Apply([waiting], "bob");
        var s = Assert.Single(result);
        Assert.True(s.Expanded);
        Assert.True(s.Collapsible);
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public void Apply_with_blank_query_returns_input_unchanged()
    {
        var waiting = new InboxSection("Waiting on others", []) { Id = "waiting", Collapsible = true, Expanded = false, Count = 9 };
        IReadOnlyList<InboxSection> input = [waiting];
        Assert.Same(input, InboxFilter.Apply(input, "  "));
    }

    [Fact]
    public void Apply_with_no_match_returns_empty() =>
        Assert.Empty(InboxFilter.Apply([new InboxSection("Approvals", [Api12]) { Id = "ApprovalPending" }], "nothing-like-this"));

    [Fact]
    public void Apply_leaves_the_source_sections_untouched()
    {
        var s = new InboxSection("Review requests", [Api12, Web7]) { Id = "ReviewRequested" };
        _ = InboxFilter.Apply([s], "api");
        Assert.Equal(2, s.Rows.Count);
    }
}

public class BatchApprovalTests
{
    static InboxRow R(string key, bool stale = false, params ItemAction[] actions) =>
        new(key, "GH", "contoso/api", "T", "https://u", "1h", stale, null, null, null, null, false, null, false, true, [], null, null,
            actions.Select(a => new RowActionButton(a, a.ToString(), true, null)).ToList(), RowMode.Collapsed, null, null, false, null, null,
            "contoso/api");

    static InboxSection Approvals(params InboxRow[] rows) => new("Approvals", rows) { Id = "ApprovalPending" };

    static readonly ItemAction[] Both = [ItemAction.Approve, ItemAction.Reject];

    [Fact]
    public void Eligible_are_non_stale_rows_with_approve_in_display_order()
    {
        var s = Approvals(R("a", false, Both), R("b", true, Both), R("c", false, ItemAction.ApprovePr), R("d", false, Both), R("e"));
        Assert.Equal(["a", "d"], BatchApproval.Eligible(s).Select(r => r.Key));
    }

    [Fact]
    public void Other_sections_have_no_eligible_rows() =>
        Assert.Empty(BatchApproval.Eligible(new InboxSection("Review requests", [R("a", false, Both), R("b", false, Both)]) { Id = "ReviewRequested" }));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void Buttons_need_two_eligible_rows(int count, bool shown)
    {
        var s = Approvals(Enumerable.Range(0, count).Select(i => R("k" + i, false, Both)).ToArray());
        Assert.Equal(shown, BatchApproval.ShowsButtons(s));
    }

    [Fact]
    public void Stale_rows_do_not_count_toward_the_threshold() =>
        Assert.False(BatchApproval.ShowsButtons(Approvals(R("a", false, Both), R("b", true, Both))));

    [Fact]
    public void Caller_supplied_actions_override_row_buttons()
    {
        var s = Approvals(R("a"), R("b"), R("c"));
        Assert.True(BatchApproval.ShowsButtons(s, _ => Both));
        Assert.Empty(BatchApproval.Eligible(s, _ => [ItemAction.Reject]));
    }

    [Fact]
    public void Summary_counts_outcomes()
    {
        BatchItemResult[] results =
        [
            new("a", BatchOutcome.Ok), new("b", BatchOutcome.Ok), new("c", BatchOutcome.Ok),
            new("d", BatchOutcome.Stale, "This changed since you looked — refreshing"),
        ];
        Assert.Equal("3 approved · 1 changed since you looked · 0 failed", BatchApproval.Summary(results));
        Assert.Equal("3 rejected · 1 changed since you looked · 0 failed", BatchApproval.Summary(results, ItemAction.Reject));
    }

    [Fact]
    public void Summary_counts_failures_and_empty()
    {
        Assert.Equal("1 approved · 0 changed since you looked · 2 failed",
            BatchApproval.Summary([new("a", BatchOutcome.Ok), new("b", BatchOutcome.Failed), new("c", BatchOutcome.Failed, "boom")]));
        Assert.Equal("0 approved · 0 changed since you looked · 0 failed", BatchApproval.Summary([]));
    }

    [Fact]
    public void Confirm_label_counts_checked_items()
    {
        Assert.Equal("Approve 3", BatchApproval.ConfirmLabel(ItemAction.Approve, 3));
        Assert.Equal("Reject 2", BatchApproval.ConfirmLabel(ItemAction.Reject, 2));
    }
}
