using System.Text.Json;

namespace GhNotify.Core;

public sealed class AppState
{
    public Dictionary<string, List<InboxItem>> Snapshot { get; set; } = new();
    public List<AgentRun> AgentRuns { get; set; } = [];
}

public sealed class StateStore(string path)
{
    readonly object _gate = new();

    public AppState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(path)) return new AppState();
            try
            {
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json.Options) ?? new AppState();
            }
            catch (JsonException ex)
            {
                Log.Error($"state.json unreadable, starting fresh: {ex.Message}");
                File.Move(path, path + ".bad", overwrite: true);
                return new AppState();
            }
        }
    }

    public void Save(AppState state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, Json.Options));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
