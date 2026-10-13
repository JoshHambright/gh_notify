using System.Text.RegularExpressions;

namespace GhNotify.Agents;

public static class RemoteUrl
{
    static readonly Regex Scp = new(@"^(?:[^@/]+@)?(?<host>[^:/\\]{2,}):(?<path>[^/\\].*)$", RegexOptions.Compiled);

    public static string? Normalize(string url)
    {
        url = url.Trim();
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url[..^4];
        url = url.TrimEnd('/');

        string host, path;
        if (url.Contains("://"))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            host = u.Host;
            path = Uri.UnescapeDataString(u.AbsolutePath);
        }
        else if (Scp.Match(url) is { Success: true } m)
        {
            host = m.Groups["host"].Value;
            path = Uri.UnescapeDataString(m.Groups["path"].Value);
        }
        else return null;

        host = host.ToLowerInvariant();
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        string? result = null;

        if (host is "github.com" or "ssh.github.com")
            result = segs.Count >= 2 ? $"github.com/{segs[0]}/{segs[1]}" : null;
        else if (host is "ssh.dev.azure.com" or "vs-ssh.visualstudio.com")
            result = segs.Count == 4 && segs[0] == "v3" ? $"dev.azure.com/{segs[1]}/{segs[2]}/{segs[3]}" : null;
        else if (host == "dev.azure.com")
            result = Ado(segs.Skip(1).ToList(), segs.FirstOrDefault());
        else if (host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
        {
            if (segs.Count > 0 && segs[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase)) segs.RemoveAt(0);
            result = Ado(segs, host.Split('.')[0]);
        }
        return result?.ToLowerInvariant();
    }

    /// <summary>segs after the org: "project/_git/repo" or "_git/repo" (project named like the repo).</summary>
    static string? Ado(List<string> segs, string? org)
    {
        if (org is null) return null;
        if (segs.Count == 3 && segs[1] == "_git") return $"dev.azure.com/{org}/{segs[0]}/{segs[2]}";
        if (segs.Count == 2 && segs[0] == "_git") return $"dev.azure.com/{org}/{segs[1]}/{segs[1]}";
        return null;
    }
}
