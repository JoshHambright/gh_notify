namespace GhNotify.Core;

public sealed record ConfigError(string Field, string Message);

public static class ConfigValidator
{
    public static IReadOnlyList<ConfigError> Errors(AppConfig c)
    {
        var e = new List<ConfigError>();
        void Add(string field, string message) => e.Add(new ConfigError(field, message));
        bool NotNull(object? v, string field) { if (v is null) { Add(field, $"{field} must not be null"); return false; } return true; }
        bool Entries<T>(List<T>? list, string field)
        {
            if (!NotNull(list, field)) return false;
            var ok = true;
            for (var i = 0; i < list!.Count; i++) ok &= NotNull(list[i], $"{field}[{i}]");
            return ok;
        }

        if (c.PollSeconds < 30) Add("pollSeconds", "pollSeconds must be at least 30");
        if (c.MaxConcurrentAgents < 1) Add("maxConcurrentAgents", "maxConcurrentAgents must be at least 1");
        if (c.AgentTimeoutMinutes < 1) Add("agentTimeoutMinutes", "agentTimeoutMinutes must be at least 1");

        if (NotNull(c.WorkHours, "workHours") && Entries(c.WorkHours.Days, "workHours.days"))
        {
            try { WorkHours.Validate(c.WorkHours); }
            catch (InvalidDataException ex) { Add("workHours", ex.Message); }
        }
        if (NotNull(c.GitHub, "github")) Entries(c.GitHub.WatchRepos, "github.watchRepos");
        if (NotNull(c.Ado, "ado")) { Entries(c.Ado.Orgs, "ado.orgs"); Entries(c.Ado.ExcludeOrgs, "ado.excludeOrgs"); }
        Entries(c.RepoRoots, "repoRoots");
        Entries(c.AgentAllowedTools, "agentAllowedTools");

        if (NotNull(c.Display, "display") && NotNull(c.Display.RepoAliases, "display.repoAliases"))
        {
            foreach (var (k, v) in c.Display.RepoAliases) NotNull(v, $"display.repoAliases.{k}");
            if (c.Display.MaxRepoChars < 12) Add("display.maxRepoChars", "display.maxRepoChars must be at least 12");
        }

        if (Entries(c.Hooks, "hooks"))
            for (var i = 0; i < c.Hooks.Count; i++)
                if (Entries(c.Hooks[i].Run, $"hooks[{i}].run") && (c.Hooks[i].Run.Count == 0 || string.IsNullOrWhiteSpace(c.Hooks[i].Run[0])))
                    Add($"hooks[{i}].run", $"hooks[{i}].run must name a command");

        var profilesOk = NotNull(c.AgentProfiles, "agentProfiles");
        if (profilesOk)
            foreach (var (name, p) in c.AgentProfiles)
            {
                if (!NotNull(p, $"agentProfiles.{name}")) { profilesOk = false; continue; }
                Entries(p.AllowedTools, $"agentProfiles.{name}.allowedTools");
                Entries(p.DisallowedTools, $"agentProfiles.{name}.disallowedTools");
                var hasPrompt = !string.IsNullOrWhiteSpace(p.Prompt);
                var hasCommand = p.Command is not null;
                if (!hasPrompt && !hasCommand)
                    Add($"agentProfiles.{name}", $"agentProfiles.{name}.prompt must not be empty"); // legacy message, kept byte-identical
                else if (hasPrompt && hasCommand)
                    Add($"agentProfiles.{name}", $"agentProfiles.{name} needs exactly one of prompt or command");
                else if (hasCommand && (!Entries(p.Command, $"agentProfiles.{name}.command") || p.Command!.Count == 0 || string.IsNullOrWhiteSpace(p.Command[0])))
                    Add($"agentProfiles.{name}.command", $"agentProfiles.{name}.command must name a program");
            }

        var known = profilesOk ? ProfileCatalog.All(c) : ProfileCatalog.BuiltIn;
        if (Entries(c.Rules, "rules"))
            for (var i = 0; i < c.Rules.Count; i++)
                if (c.Rules[i].Profile is { } rp && !known.ContainsKey(rp))
                    Add($"rules[{i}].profile", $"rules[{i}].profile '{rp}' is not a known agent profile");

        if (NotNull(c.DefaultProfiles, "defaultProfiles"))
            foreach (var (kind, profile) in c.DefaultProfiles)
            {
                if (!Enum.TryParse<ItemKind>(kind, ignoreCase: true, out _))
                    Add($"defaultProfiles.{kind}", $"defaultProfiles: '{kind}' is not an item kind");
                else if (profile is null || !known.ContainsKey(profile))
                    Add($"defaultProfiles.{kind}", $"defaultProfiles.{kind}: '{profile}' is not a known agent profile");
            }

        if (NotNull(c.Updates, "updates") && c.Updates.CheckHours is < 1 or > 168)
            Add("updates.checkHours", "updates.checkHours must be between 1 and 168");

        if (NotNull(c.Notifications, "notifications") && Entries(c.Notifications.Sounds, "notifications.sounds"))
            for (var i = 0; i < c.Notifications.Sounds.Count; i++)
                if (SoundError(c.Notifications.Sounds[i].Sound) is { } msg) Add($"notifications.sounds[{i}].sound", msg);

        if (NotNull(c.Filters, "filters"))
            foreach (var (list, name) in new[] { (c.Filters.Mute, "filters.mute"), (c.Filters.Hide, "filters.hide") })
                if (Entries(list, name))
                    for (var i = 0; i < list.Count; i++)
                        if (!FilterPattern.TryParse(list[i], out _, out var err)) Add($"{name}[{i}]", err!);

        return e;
    }

    public static string? SoundError(string? sound) => sound switch
    {
        null or "" => "sound must not be empty",
        "default" or "none" => null,
        _ when sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) =>
            SoundNames.BuiltIn.Contains(sound["builtin:".Length..].ToLowerInvariant()) ? null
                : $"unknown built-in sound '{sound}' (use one of {string.Join(", ", SoundNames.BuiltIn)})",
        _ when !Path.IsPathRooted(sound) => $"'{sound}' must be an absolute path to a .wav file",
        _ when !sound.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) => $"'{sound}' must be a .wav file",
        _ => null,
    };
}
