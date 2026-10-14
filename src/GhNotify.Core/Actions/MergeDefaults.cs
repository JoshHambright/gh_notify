namespace GhNotify.Core;

/// <summary>Default merge method selection (v1.3 spec §2).</summary>
public static class MergeDefaults
{
    static readonly MergeMethod[] Fallback = [MergeMethod.Squash, MergeMethod.Merge, MergeMethod.Rebase];

    /// <summary>The configured method if allowed; otherwise Squash if allowed; otherwise the first allowed in the order Squash, Merge, Rebase.</summary>
    public static MergeMethod Pick(IReadOnlyList<MergeMethod> allowed, MergeMethod? configured)
    {
        if (configured is { } c && allowed.Contains(c)) return c;
        foreach (var m in Fallback)
            if (allowed.Contains(m)) return m;
        return MergeMethod.Squash;
    }
}
