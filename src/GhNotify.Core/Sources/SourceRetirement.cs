namespace GhNotify.Core;

/// <summary>Pure rules for which sources' items must be retired.</summary>
public static class SourceRetirement
{
    public const string AdoPseudoSource = "ADO";

    public static bool IsDisabled(string name, AppConfig c)
    {
        if (name == "GitHub") return !c.GitHub.Enabled;
        if (name == AdoPseudoSource) return !c.Ado.Enabled;
        if (name.StartsWith("ADO:", StringComparison.Ordinal))
        {
            var org = name[4..];
            return !c.Ado.Enabled
                || c.Ado.ExcludeOrgs.Contains(org, StringComparer.OrdinalIgnoreCase)
                || (!c.Ado.DiscoverOrgs && !c.Ado.Orgs.Contains(org, StringComparer.OrdinalIgnoreCase));
        }
        return false;
    }

    /// <summary>The "ADO" pseudo-source is superseded once any real "ADO:&lt;org&gt;" source is healthy.</summary>
    public static bool ShouldRetireAdoPseudoSource(IEnumerable<SourceStatus> statuses) =>
        statuses.Any(s => s.Healthy && s.Name.StartsWith("ADO:", StringComparison.Ordinal));
}
