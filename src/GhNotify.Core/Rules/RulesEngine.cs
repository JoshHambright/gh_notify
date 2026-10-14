namespace GhNotify.Core;

public sealed record AgentDecision(AgentMode Mode, string? ProfileName, AgentProfile? Profile);

public static class RulesEngine
{
    public static AgentDecision Decide(InboxItem item, AppConfig config)
    {
        var rule = config.Rules.FirstOrDefault(r => ItemMatcher.Matches(r.Match, item));
        var mode = rule?.Agent ?? AgentMode.Button;
        var name = rule?.Profile ?? DefaultProfileFor(item.Kind, config);
        if (mode == AgentMode.None || name is null) return new(AgentMode.None, name, null);
        return ProfileCatalog.All(config).TryGetValue(name, out var profile)
            ? new(mode, name, profile)
            : new(AgentMode.None, name, null);
    }

    public static string? DefaultProfileFor(ItemKind kind, AppConfig cfg)
    {
        foreach (var (k, v) in cfg.DefaultProfiles)
            if (string.Equals(k, kind.ToString(), StringComparison.OrdinalIgnoreCase)) return v;
        return kind switch
        {
            ItemKind.ReviewRequested => "review",
            ItemKind.MyPrCiFailing or ItemKind.MyPrNeedsResponse => "diagnose-ci",
            _ => null,
        };
    }
}
