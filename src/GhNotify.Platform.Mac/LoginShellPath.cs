using GhNotify.Core;

namespace GhNotify.Platform.Mac;

/// <summary>
/// Apps started from Finder or as a login item get a minimal PATH (/usr/bin:/bin:/usr/sbin:/sbin), so gh, az and claude from
/// Homebrew would not be found. At startup, ask the user's login shell for its PATH and append what is missing.
/// </summary>
public static class LoginShellPath
{
    static readonly string[] Extras = ["/opt/homebrew/bin", "/usr/local/bin"];

    /// <summary>Never throws; a failure is logged and PATH stays as it was.</summary>
    public static void Merge(IProcessRunner runner, TimeSpan? timeout = null)
    {
        string? fromShell = null;
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s && s.StartsWith('/') ? s : "/bin/zsh";
            var r = runner.RunAsync(new ProcessSpec(shell, ["-l", "-c", "printenv PATH"], Timeout: timeout ?? TimeSpan.FromSeconds(3)))
                .GetAwaiter().GetResult();
            if (r.TimedOut) Log.Error("the login shell took too long to report its PATH; using the default PATH");
            else if (r.ExitCode != 0) Log.Error($"the login shell exited {r.ExitCode} reporting its PATH; using the default PATH");
            else fromShell = LastLine(r.StdOut);
        }
        catch (Exception ex)
        {
            Log.Error("could not read the login shell's PATH", ex);
        }
        var merged = Combine(Environment.GetEnvironmentVariable("PATH"), fromShell, Directory.Exists);
        Environment.SetEnvironmentVariable("PATH", merged);
        Log.Info($"PATH: {merged}");
    }

    /// <summary>The current entries first, then the shell's entries that are new, then Homebrew's folders if they exist. Empty and
    /// relative entries are dropped.</summary>
    internal static string Combine(string? current, string? fromShell, Func<string, bool> dirExists)
    {
        var result = new List<string>();
        void Add(string entry)
        {
            entry = entry.Trim();
            if (entry.Length > 0 && entry.StartsWith('/') && !result.Contains(entry)) result.Add(entry);
        }
        foreach (var e in (current ?? "").Split(':')) Add(e);
        foreach (var e in (fromShell ?? "").Split(':')) Add(e);
        foreach (var e in Extras) if (dirExists(e)) Add(e);
        return string.Join(':', result);
    }

    /// <summary>A noisy profile may print before printenv does; the PATH is what follows the last newline.</summary>
    internal static string LastLine(string output)
    {
        var trimmed = output.TrimEnd('\r', '\n');
        var i = trimmed.LastIndexOf('\n');
        return i < 0 ? trimmed : trimmed[(i + 1)..];
    }
}
