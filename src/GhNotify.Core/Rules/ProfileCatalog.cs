using System.Text.RegularExpressions;

namespace GhNotify.Core;

public static class ProfileCatalog
{
    public static readonly IReadOnlyDictionary<string, AgentProfile> BuiltIn =
        new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["review"] = new()
            {
                Prompt = "Review the pull request at {url}. Read the diff and the surrounding code. List bugs, risky changes and missing tests, " +
                         "most important first, with file:line. Do not post, approve, comment or push anything. " +
                         "End with a 1–3 line summary starting \"SUMMARY:\".",
            },
            ["diagnose-ci"] = new()
            {
                Prompt = "The pull request at {url} has failing checks. Read the failing check logs and the relevant code, find the root cause " +
                         "of each failure, and suggest the smallest fix. Make no commits, pushes or comments. " +
                         "End with a 1–3 line summary starting \"SUMMARY:\".",
            },
            ["peer-review"] = new()
            {
                RequiresSkill = "peer-review",
                Prompt = "/peer-review {url} — run unattended: complete the review through writing the findings report, then STOP. " +
                         "Do not post anything. End with a 1–3 line summary starting \"SUMMARY:\".",
            },
            ["pr-shepherd"] = new()
            {
                RequiresSkill = "pr-shepherd",
                Prompt = "/pr-shepherd {url} — diagnose only: find the real reason for each failure and triage comments. " +
                         "Make no commits, pushes, or thread replies. End with a 1–3 line summary starting \"SUMMARY:\".",
            },
        };

    public static IReadOnlyDictionary<string, AgentProfile> All(AppConfig c)
    {
        var all = new Dictionary<string, AgentProfile>(BuiltIn, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, profile) in c.AgentProfiles) all[name] = profile;
        return all;
    }
}

public static class PromptTemplate
{
    static readonly Regex Placeholder = new(@"\{(url|repo|number|title|kind|provider|author|detail)\}", RegexOptions.Compiled);

    /// <summary>Single pass, so values that themselves contain "{url}" are never re-substituted.</summary>
    public static string Fill(string template, InboxItem item) =>
        Placeholder.Replace(template, m => m.Groups[1].Value switch
        {
            "url" => item.Url,
            "repo" => item.Repo,
            "number" => item.Number?.ToString() ?? "",
            "title" => item.Title,
            "kind" => item.Kind.ToString(),
            "provider" => item.Provider.ToString(),
            "author" => item.Author,
            _ => item.Detail ?? "",
        });
}
