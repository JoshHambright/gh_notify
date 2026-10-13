using System.Text.RegularExpressions;

namespace GhNotify.Core;

public static class ItemMatcher
{
    public static bool Matches(ItemMatch? m, InboxItem item)
    {
        if (m is null) return true;
        if (m.Provider is { } p && p != item.Provider) return false;
        if (m.Kind is { } k && k != item.Kind) return false;
        if (m.Repo is { } r && !Glob(r, item.Repo)) return false;
        if (m.Author is { } a && !string.Equals(a, item.Author, StringComparison.OrdinalIgnoreCase)) return false;
        if (m.MaxChangedLines is { } max && (item.ChangedLines is not { } n || n > max)) return false;
        return true;
    }

    public static bool Glob(string pattern, string text) =>
        Regex.IsMatch(text,
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
