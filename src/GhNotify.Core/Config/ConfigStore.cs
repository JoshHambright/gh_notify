using System.Text.Json;

namespace GhNotify.Core;

/// <summary>Loads config.json; on any error keeps the last good config and exposes the error.</summary>
public sealed class ConfigStore(string path)
{
    public string Path { get; } = path;
    public AppConfig Current { get; private set; } = new();
    public string? Error { get; private set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(new AppConfig(), Json.Options));
                Current = new AppConfig();
                Error = null;
                return;
            }
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path), Json.Options)
                         ?? throw new InvalidDataException("config.json is empty");
            Validate(config);
            Current = config;
            Error = null;
        }
        catch (Exception ex)
        {
            // Any failure (bad JSON, nulls, IO, unexpected) keeps the last good config; a bad edit must never stop the app.
            Error = $"config.json: {ex.Message}";
            Log.Error(Error);
        }
    }

    public static void Validate(AppConfig c)
    {
        RejectNulls(c);
        if (c.PollSeconds < 30) throw new InvalidDataException("pollSeconds must be at least 30");
        if (c.MaxConcurrentAgents < 1) throw new InvalidDataException("maxConcurrentAgents must be at least 1");
        if (c.AgentTimeoutMinutes < 1) throw new InvalidDataException("agentTimeoutMinutes must be at least 1");
        if (c.Display.MaxRepoChars < 12) throw new InvalidDataException("display.maxRepoChars must be at least 12");
        for (var i = 0; i < c.Hooks.Count; i++)
            if (c.Hooks[i].Run.Count == 0 || string.IsNullOrWhiteSpace(c.Hooks[i].Run[0]))
                throw new InvalidDataException($"hooks[{i}].run must name a command");
        WorkHours.Validate(c.WorkHours);
        foreach (var (name, profile) in c.AgentProfiles)
            if (string.IsNullOrWhiteSpace(profile.Prompt))
                throw new InvalidDataException($"agentProfiles.{name}.prompt must not be empty");
        var profiles = ProfileCatalog.All(c);
        for (var i = 0; i < c.Rules.Count; i++)
            if (c.Rules[i].Profile is { } p && !profiles.ContainsKey(p))
                throw new InvalidDataException($"rules[{i}].profile '{p}' is not a known agent profile");
    }

    /// <summary>JSON "null" overrides the property defaults; reject it so nothing downstream dereferences a null.</summary>
    static void RejectNulls(AppConfig c)
    {
        NotNull(c.WorkHours, "workHours");
        Entries(c.WorkHours.Days, "workHours.days");
        NotNull(c.GitHub, "github");
        Entries(c.GitHub.WatchRepos, "github.watchRepos");
        NotNull(c.Ado, "ado");
        Entries(c.Ado.Orgs, "ado.orgs");
        Entries(c.Ado.ExcludeOrgs, "ado.excludeOrgs");
        Entries(c.RepoRoots, "repoRoots");
        Entries(c.AgentAllowedTools, "agentAllowedTools");
        Entries(c.Rules, "rules");
        NotNull(c.AgentProfiles, "agentProfiles");
        foreach (var (name, profile) in c.AgentProfiles)
        {
            NotNull(profile, $"agentProfiles.{name}");
            Entries(profile.AllowedTools, $"agentProfiles.{name}.allowedTools");
            Entries(profile.DisallowedTools, $"agentProfiles.{name}.disallowedTools");
        }
        Entries(c.Hooks, "hooks");
        for (var i = 0; i < c.Hooks.Count; i++) Entries(c.Hooks[i].Run, $"hooks[{i}].run");
        NotNull(c.Display, "display");
        NotNull(c.Display.RepoAliases, "display.repoAliases");
        foreach (var (k, v) in c.Display.RepoAliases) NotNull(v, $"display.repoAliases.{k}");
    }

    static void NotNull(object? value, string field)
    {
        if (value is null) throw new InvalidDataException($"{field} must not be null");
    }

    static void Entries<T>(List<T>? list, string field)
    {
        NotNull(list, field);
        for (var i = 0; i < list!.Count; i++) NotNull(list[i], $"{field}[{i}]");
    }
}
