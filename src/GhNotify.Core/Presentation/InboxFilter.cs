namespace GhNotify.Core;

/// <summary>Popup search (v1.3 spec §6). Applied after presentation; the tray icon and badge are never filtered.</summary>
public static class InboxFilter
{
    static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    public static bool Matches(InboxRow row, string? query)
    {
        foreach (var term in Terms(query))
            if (!TermMatches(row, term)) return false;
        return true;
    }

    /// <summary>
    /// Keeps matching rows, drops empty sections, shows filtered counts and expands collapsible sections while the query is non-blank.
    /// A blank query returns the sections unchanged. Collapsed sections presented without rows have nothing to match, so the caller
    /// must present them expanded while searching.
    /// </summary>
    public static IReadOnlyList<InboxSection> Apply(IReadOnlyList<InboxSection> sections, string? query)
    {
        if (Terms(query).Length == 0) return sections;
        var result = new List<InboxSection>();
        foreach (var s in sections)
        {
            var rows = s.Rows.Where(r => Matches(r, query)).ToList();
            if (rows.Count == 0) continue;
            result.Add(s with { Rows = rows, Count = rows.Count, Expanded = s.Collapsible || s.Expanded });
        }
        return result;
    }

    static string[] Terms(string? query) => (query ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    static bool TermMatches(InboxRow row, string term)
    {
        if (Has(row.RepoDisplay, term) || Has(row.Repo, term) || Has(row.Title, term) || Has(row.Author, term)) return true;
        var digits = term.TrimStart('#');
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) && row.Number is { } n && n.TrimStart('#') == digits;
    }

    static bool Has(string? text, string term) => text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
