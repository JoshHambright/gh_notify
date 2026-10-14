using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class MergeStatementTests
{
    [Theory]
    [InlineData(MergeMethod.Squash, "Squash and merge")]
    [InlineData(MergeMethod.Merge, "Merge")]
    [InlineData(MergeMethod.Rebase, "Rebase and merge")]
    public void Build_uses_the_method_wording(MergeMethod method, string verb) =>
        Assert.Equal($"{verb} PR #12 into main in contoso/api at abc1234", MergeStatement.Build(method, 12, "main", "contoso/api", "abc1234"));

    [Theory]
    [InlineData(MergeMethod.Squash, MergeMethod.Rebase, "Rebase and merge PR #12 into main")]
    [InlineData(MergeMethod.Rebase, MergeMethod.Merge, "Merge PR #12 into main")]
    [InlineData(MergeMethod.Merge, MergeMethod.Squash, "Squash and merge PR #12 into main")]
    [InlineData(MergeMethod.Squash, MergeMethod.Squash, "Squash and merge PR #12 into main")]
    public void WithMethod_swaps_the_leading_verb(MergeMethod from, MergeMethod to, string expected) =>
        Assert.Equal(expected, MergeStatement.WithMethod(MergeStatement.Build(from, 12, "main", "r", "s"), to)[..expected.Length]);

    [Fact]
    public void WithMethod_leaves_other_text_alone() =>
        Assert.Equal("Approve PR #3", MergeStatement.WithMethod("Approve PR #3", MergeMethod.Rebase));
}
