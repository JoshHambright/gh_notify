namespace GhNotify.Core;

public static class RepoNames
{
    const char Ellipsis = '…';

    /// <summary>
    /// Short display form of a repo path ("org/repo" or "org/project/repo"): applies the longest matching alias prefix,
    /// then, if still longer than <paramref name="maxChars"/>, middle-ellipsizes while keeping the last segment intact.
    /// </summary>
    public static string Display(string repo, IReadOnlyDictionary<string, string>? aliases, int maxChars)
    {
        var name = ApplyAlias(repo, aliases);
        if (name.Length <= maxChars) return name;

        var slash = name.LastIndexOf('/');
        var last = slash >= 0 ? name[(slash + 1)..] : name;
        if (slash >= 0)
        {
            var tail = name[slash..]; // "/repo"
            var keep = maxChars - tail.Length - 1;
            if (keep >= 1) return name[..keep] + Ellipsis + tail;
            if (tail.Length + 1 <= maxChars) return Ellipsis + tail;
            if (last.Length <= maxChars) return last;
        }
        var head = maxChars / 2;
        var end = maxChars - 1 - head;
        return last[..head] + Ellipsis + last[^end..];
    }

    static string ApplyAlias(string repo, IReadOnlyDictionary<string, string>? aliases)
    {
        if (aliases is null || aliases.Count == 0) return repo;
        string? bestKey = null;
        foreach (var key in aliases.Keys)
        {
            var k = key.TrimEnd('/');
            if (k.Length == 0) continue;
            var matches = repo.Length == k.Length
                ? repo.Equals(k, StringComparison.OrdinalIgnoreCase)
                : repo.Length > k.Length && repo[k.Length] == '/' && repo.StartsWith(k, StringComparison.OrdinalIgnoreCase);
            if (matches && (bestKey is null || k.Length > bestKey.TrimEnd('/').Length)) bestKey = key;
        }
        return bestKey is null ? repo : aliases[bestKey] + repo[bestKey.TrimEnd('/').Length..];
    }
}
