using GhNotify.Core;

namespace GhNotify.Agents;

public interface ICommandRunner
{
    Task<AgentOutcome> RunAsync(string workingDir, IReadOnlyList<string> command, InboxItem item, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Runs a command profile: argv with placeholders filled per argument (never through a shell), item data in GHN_* env vars.</summary>
public sealed class CommandRunner(IProcessRunner runner) : ICommandRunner
{
    public const int MaxOutput = 64 * 1024;

    public async Task<AgentOutcome> RunAsync(string workingDir, IReadOnlyList<string> command, InboxItem item, TimeSpan timeout, CancellationToken ct)
    {
        var args = command.Skip(1).Select(a => PromptTemplate.Fill(a, item)).ToList();
        try
        {
            var r = await runner.RunAsync(new ProcessSpec(command[0], args, workingDir, null, EnvFor(item), timeout), ct);
            return Parse(r);
        }
        catch (FileNotFoundException)
        {
            return new AgentOutcome(false, null, null, null, $"'{command[0]}' not found");
        }
        catch (ArgumentException ex)
        {
            return new AgentOutcome(false, null, null, null, ex.Message);
        }
    }

    public static IReadOnlyDictionary<string, string> EnvFor(InboxItem i) => new Dictionary<string, string>
    {
        ["GHN_URL"] = i.Url,
        ["GHN_REPO"] = i.Repo,
        ["GHN_NUMBER"] = i.Number?.ToString() ?? "",
        ["GHN_TITLE"] = i.Title,
        ["GHN_KIND"] = i.Kind.ToString(),
        ["GHN_PROVIDER"] = i.Provider.ToString(),
        ["GHN_AUTHOR"] = i.Author,
        ["GHN_ITEM_KEY"] = i.Key,
    };

    public static AgentOutcome Parse(ProcessResult r)
    {
        var text = r.StdErr.Length == 0 ? r.StdOut : r.StdOut + "\n" + r.StdErr;
        if (text.Length > MaxOutput) text = text[^MaxOutput..];
        if (r.TimedOut) return new AgentOutcome(false, null, null, null, "timed out") { ResultText = text };
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var summaryLine = lines.FirstOrDefault(l => l.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase));
        var summary = summaryLine is not null ? summaryLine["SUMMARY:".Length..].Trim() : lines.LastOrDefault();
        if (summary is { Length: > 200 }) summary = summary[..200] + "…";
        var ok = r.ExitCode == 0;
        return new AgentOutcome(ok, null, summary, null, ok ? null : $"exit {r.ExitCode}") { ResultText = text };
    }
}
