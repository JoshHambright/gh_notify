namespace GhNotify.Core;

/// <summary>The merge confirm statement, worded from the method that will actually be used.</summary>
public static class MergeStatement
{
    static readonly MergeMethod[] All = [MergeMethod.Merge, MergeMethod.Squash, MergeMethod.Rebase];

    public static string Verb(MergeMethod m) => m switch
    {
        MergeMethod.Squash => "Squash and merge",
        MergeMethod.Rebase => "Rebase and merge",
        _ => "Merge",
    };

    /// <summary>"Squash and merge PR #12 into main in contoso/api at abc1234".</summary>
    public static string Build(MergeMethod method, int number, string target, string repo, string sha7) =>
        $"{Verb(method)} PR #{number} into {target} in {repo} at {sha7}";

    /// <summary>Rewrites the leading verb of a statement built by <see cref="Build"/> for another method; other text is returned unchanged.</summary>
    public static string WithMethod(string statement, MergeMethod method)
    {
        foreach (var m in All.OrderByDescending(m => Verb(m).Length))
        {
            var verb = Verb(m);
            if (statement.StartsWith(verb + " PR #", StringComparison.Ordinal))
                return Verb(method) + statement[verb.Length..];
        }
        return statement;
    }
}
