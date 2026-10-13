namespace GhNotify.Core;

public sealed record AgentDecision(AgentMode Mode, string? ProfileName, AgentProfile? Profile);

public static class RulesEngine
{
    public static AgentDecision Decide(InboxItem item, AppConfig config)
    {
        var rule = config.Rules.FirstOrDefault(r => ItemMatcher.Matches(r.Match, item));
        var mode = rule?.Agent ?? AgentMode.Button;
        var name = rule?.Profile ?? DefaultProfileFor(item.Kind);
        if (mode == AgentMode.None || name is null) return new(AgentMode.None, name, null);
        return ProfileCatalog.All(config).TryGetValue(name, out var profile)
            ? new(mode, name, profile)
            : new(AgentMode.None, name, null);
    }

    public static string? DefaultProfileFor(ItemKind kind) => kind switch
    {
        ItemKind.ReviewRequested => "peer-review",
        ItemKind.MyPrCiFailing or ItemKind.MyPrNeedsResponse => "pr-shepherd",
        _ => null,
    };
}
