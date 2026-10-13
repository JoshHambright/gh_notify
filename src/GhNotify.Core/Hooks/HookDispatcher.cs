using System.Text.Json;

namespace GhNotify.Core;

public sealed class HookDispatcher(IProcessRunner runner, Func<AppConfig> config)
{
    public Task DispatchAsync(HookEvent ev, InboxItem item, AgentRun? run = null)
    {
        var hooks = config().Hooks
            .Where(h => h.Event == ev && h.Run.Count > 0 && ItemMatcher.Matches(h.Match, item))
            .ToList();
        if (hooks.Count == 0) return Task.CompletedTask;

        var payload = JsonSerializer.Serialize(new { @event = ev.ToString(), item, agent = run }, Json.Options);
        var env = new Dictionary<string, string>
        {
            ["GHN_EVENT"] = ev.ToString(),
            ["GHN_KEY"] = item.Key,
            ["GHN_URL"] = item.Url,
            ["GHN_KIND"] = item.Kind.ToString(),
            ["GHN_REPO"] = item.Repo,
        };
        return Task.WhenAll(hooks.Select(h => RunOne(ev, h, payload, env)));
    }

    async Task RunOne(HookEvent ev, HookConfig hook, string payload, IReadOnlyDictionary<string, string> env)
    {
        var name = string.Join(' ', hook.Run);
        try
        {
            var r = await runner.RunAsync(new ProcessSpec(hook.Run[0], hook.Run.Skip(1).ToList(),
                StdIn: payload, Env: env, Timeout: TimeSpan.FromSeconds(60)));
            if (r.TimedOut || r.ExitCode != 0)
                Log.Error($"hook '{name}' for {ev} exited {r.ExitCode}{(r.TimedOut ? " (timed out)" : "")}: {r.StdErr.Trim()}");
        }
        catch (Exception ex)
        {
            Log.Error($"hook '{name}' for {ev} failed to run", ex);
        }
    }
}
