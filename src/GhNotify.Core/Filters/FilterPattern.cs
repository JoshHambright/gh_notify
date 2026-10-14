namespace GhNotify.Core;

/// <summary>"[gh:|ado:]seg/seg…" — matches an item whose path starts with these segments; '*' is a within-segment wildcard.</summary>
public sealed class FilterPattern
{
    public string Text { get; }
    public Provider? Provider { get; }
    readonly string[] _segments;

    FilterPattern(string text, Provider? provider, string[] segments)
    {
        Text = text;
        Provider = provider;
        _segments = segments;
    }

    public static bool TryParse(string? text, out FilterPattern? pattern, out string? error)
    {
        pattern = null;
        error = null;
        var body = (text ?? "").Trim();
        Provider? provider = null;
        var colon = body.IndexOf(':');
        if (colon >= 0)
        {
            var prefix = body[..colon].ToLowerInvariant();
            provider = prefix switch { "gh" => Core.Provider.GitHub, "ado" => Core.Provider.Ado, _ => null };
            if (provider is null) { error = $"'{text}': unknown prefix '{prefix}:' (use gh: or ado:)"; return false; }
            body = body[(colon + 1)..];
        }
        if (body.Length == 0) { error = $"'{text}': pattern is empty"; return false; }
        var segments = body.Split('/');
        if (segments.Any(s => s.Length == 0)) { error = $"'{text}': empty segment"; return false; }
        if (segments.Any(s => s.Contains("**"))) { error = $"'{text}': '**' is not supported; use one '*' per segment"; return false; }
        pattern = new FilterPattern(text!.Trim(), provider, segments);
        return true;
    }

    /// <summary>GitHub "org/repo"; ADO "org/project/repo" (approvals: "org/project/pipeline") — the item's Repo field.</summary>
    public static string PathOf(InboxItem item) => item.Repo;

    public bool Matches(InboxItem item)
    {
        if (Provider is { } p && p != item.Provider) return false;
        var path = PathOf(item).Split('/');
        if (_segments.Length > path.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
            if (!ItemMatcher.Glob(_segments[i], path[i])) return false;
        return true;
    }
}
