using System.Text.Json;

namespace GhNotify.Core;

public sealed record TimedMute(string Pattern, DateTimeOffset? Until);
public sealed record Snooze(string Key, DateTimeOffset? Until, bool UntilChange);

public sealed class AppState
{
    public Dictionary<string, List<InboxItem>> Snapshot { get; set; } = new();
    public List<AgentRun> AgentRuns { get; set; } = [];
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public List<TimedMute> Mutes { get; set; } = [];
    public List<Snooze> Snoozes { get; set; } = [];
    public bool WaitingExpanded { get; set; }
    public bool SnoozedExpanded { get; set; }
}

public sealed class StateStore(string path)
{
    readonly object _gate = new();

    /// <summary>Never throws: an unreadable or corrupt state file gives an empty state (corrupt files are set aside as .bad).</summary>
    public AppState Load()
    {
        lock (_gate)
        {
            string text;
            try
            {
                if (!File.Exists(path)) return new AppState();
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                // Locked or inaccessible: leave the file alone, it may be fine next time.
                Log.Error("state.json could not be read, starting fresh", ex);
                return new AppState();
            }
            try
            {
                var state = JsonSerializer.Deserialize<AppState>(text, Json.Options) ?? new AppState();
                state.Snapshot ??= new();
                state.AgentRuns ??= [];
                state.Mutes ??= [];
                state.Snoozes ??= [];
                foreach (var key in state.Snapshot.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
                    state.Snapshot.Remove(key);
                foreach (var list in state.Snapshot.Values) list.RemoveAll(i => i is null);
                state.AgentRuns.RemoveAll(r => r is null);
                state.Mutes.RemoveAll(m => m is null);
                state.Snoozes.RemoveAll(s => s is null);
                return state;
            }
            catch (Exception ex)
            {
                Log.Error($"state.json unreadable, starting fresh: {ex.Message}");
                try { File.Move(path, path + ".bad", overwrite: true); }
                catch (Exception moveEx) { Log.Error("could not set state.json aside", moveEx); }
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
