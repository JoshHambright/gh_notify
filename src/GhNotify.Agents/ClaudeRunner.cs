using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Agents;

public sealed record AgentOutcome(bool Success, string? SessionId, string? Summary, decimal? CostUsd, string? Error);

public interface IClaudeRunner
{
    Task<AgentOutcome> RunAsync(string workingDir, string prompt, AgentProfile profile, IReadOnlyList<string> globalAllowed,
        TimeSpan timeout, CancellationToken ct);
}

public static class AgentSafety
{
    /// <summary>Always passed to unattended runs. Best-effort second line of defence behind the prompt (spec §7 trust boundary).</summary>
    public static readonly IReadOnlyList<string> DisallowedTools =
    [
        "Bash(gh pr review:*)", "Bash(gh pr comment:*)", "Bash(gh pr merge:*)", "Bash(gh pr close:*)", "Bash(gh pr edit:*)",
        "Bash(gh pr ready:*)", "Bash(gh issue comment:*)",
        "Bash(gh api -X POST:*)", "Bash(gh api -X PATCH:*)", "Bash(gh api -X PUT:*)", "Bash(gh api -X DELETE:*)",
        "Bash(gh api --method POST:*)", "Bash(gh api --method PATCH:*)", "Bash(gh api --method PUT:*)", "Bash(gh api --method DELETE:*)",
        "Bash(git push:*)", "Bash(git commit:*)",
        "Bash(az repos pr create:*)", "Bash(az repos pr update:*)", "Bash(az repos pr set-vote:*)", "Bash(az repos pr reviewer:*)",
        "Bash(az repos pr work-item:*)", "Bash(az pipelines run:*)", "Bash(az rest:*)",
        "Bash(gh api * -f *)", "Bash(gh api * -F *)", "Bash(gh api * --field *)", "Bash(gh api * --raw-field *)", "Bash(gh api * --input *)",
        "Bash(gh pr * --body *)",
        "Bash(gh pr create:*)", "Bash(gh issue create:*)", "Bash(gh release create:*)", "Bash(gh api -f:*)", "Bash(gh api -F:*)",
        "Bash(gh api -X*)", "Bash(gh api --method=*)",
    ];
}

public sealed class ClaudeRunner(IProcessRunner runner) : IClaudeRunner
{
    public async Task<AgentOutcome> RunAsync(string workingDir, string prompt, AgentProfile profile, IReadOnlyList<string> globalAllowed,
        TimeSpan timeout, CancellationToken ct)
    {
        var result = await runner.RunAsync(new ProcessSpec("claude", BuildArgs(profile, globalAllowed),
            WorkingDirectory: workingDir, StdIn: prompt, Timeout: timeout), ct);
        return Parse(result);
    }

    public static IReadOnlyList<string> BuildArgs(AgentProfile profile, IReadOnlyList<string> globalAllowed)
    {
        var args = new List<string> { "-p", "--output-format", "json", "--permission-mode", "default" };
        var allowed = globalAllowed.Concat(profile.AllowedTools).Distinct().ToList();
        if (allowed.Count > 0)
        {
            args.Add("--allowedTools");
            args.AddRange(allowed);
        }
        args.Add("--disallowedTools");
        args.AddRange(AgentSafety.DisallowedTools.Concat(profile.DisallowedTools).Distinct());
        return args;
    }

    public static AgentOutcome Parse(ProcessResult r)
    {
        if (r.TimedOut) return new AgentOutcome(false, null, null, null, "timed out");
        var start = r.StdOut.IndexOf('{');
        try
        {
            if (start < 0) throw new JsonException("no JSON in output");
            using var doc = JsonDocument.Parse(r.StdOut[start..]);
            var root = doc.RootElement;
            var sid = root.Str("session_id");
            var text = root.Str("result");
            decimal? cost = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("total_cost_usd", out var c) && c.TryGetDecimal(out var d) ? d : null;
            if (root.Bool("is_error") || r.ExitCode != 0)
                return new AgentOutcome(false, sid, ExtractSummary(text), cost, Tail(text ?? r.StdErr));
            return new AgentOutcome(true, sid, ExtractSummary(text), cost, null);
        }
        catch (JsonException)
        {
            return new AgentOutcome(false, null, null, null, Tail(r.StdErr.Length > 0 ? r.StdErr : r.StdOut));
        }
    }

    public static string? ExtractSummary(string? result)
    {
        if (result is null) return null;
        var lines = result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        foreach (var line in Enumerable.Reverse(lines))
        {
            var bare = line.TrimStart('*', '_', ' ');
            if (bare.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase))
                return bare["SUMMARY:".Length..].Trim().Trim('*', '_').Trim();
        }
        return lines.Count == 0 ? null : Truncate(lines[^1], 200);
    }

    static string Tail(string s) => s.Length <= 500 ? s.Trim() : "…" + s[^500..].Trim();
    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
