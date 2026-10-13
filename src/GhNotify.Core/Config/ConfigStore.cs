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
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = $"config.json: {ex.Message}";
            Log.Error(Error);
        }
    }

    public static void Validate(AppConfig c)
    {
        if (c.PollSeconds < 30) throw new InvalidDataException("pollSeconds must be at least 30");
        if (c.MaxConcurrentAgents < 1) throw new InvalidDataException("maxConcurrentAgents must be at least 1");
        if (c.AgentTimeoutMinutes < 1) throw new InvalidDataException("agentTimeoutMinutes must be at least 1");
        for (var i = 0; i < c.Hooks.Count; i++)
            if (c.Hooks[i].Run.Count == 0 || string.IsNullOrWhiteSpace(c.Hooks[i].Run[0]))
                throw new InvalidDataException($"hooks[{i}].run must name a command");
    }
}
