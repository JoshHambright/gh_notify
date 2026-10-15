using System.Diagnostics;

namespace GhNotify.Core;

/// <summary>
/// Resolves a command on PATH and builds a ProcessStartInfo. On Windows it tries .exe/.cmd/.bat and runs .cmd/.bat via cmd.exe;
/// elsewhere it uses the name as is (no extensions) and runs .sh scripts through /bin/sh.
/// </summary>
public static class CommandResolver
{
    public static string? Find(string name) =>
        Find(name, Environment.GetEnvironmentVariable("PATH"), File.Exists, OperatingSystem.IsWindows());

    /// <summary>The testable core: <paramref name="path"/> is the PATH value, <paramref name="exists"/> a file-exists probe.</summary>
    public static string? Find(string name, string? path, Func<string, bool> exists, bool windows)
    {
        if (Path.IsPathRooted(name)) return exists(name) ? name : null;
        string[] exts = windows && !Path.HasExtension(name) ? [".exe", ".cmd", ".bat"] : [""];
        var separator = windows ? ';' : ':';
        var dirs = (path ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in dirs)
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), name + ext);
                if (exists(candidate)) return candidate;
            }
        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string name, IReadOnlyList<string> args)
    {
        var path = Find(name) ?? throw new FileNotFoundException($"'{name}' was not found on PATH", name);
        return CreateStartInfoFor(path, args, OperatingSystem.IsWindows());
    }

    /// <summary>Builds the start info for an already resolved <paramref name="path"/>.</summary>
    public static ProcessStartInfo CreateStartInfoFor(string path, IReadOnlyList<string> args, bool windows)
    {
        var ext = Path.GetExtension(path);
        if (windows && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // cmd /s /c "<whole line>": /s strips only the outer quotes, so quoted paths and args survive.
            foreach (var a in args)
                foreach (var bad in new[] { '"', '%', '!', '\r', '\n' })
                    if (a.Contains(bad))
                        throw new ArgumentException(
                            $"argument contains {Describe(bad)} which cannot be passed safely to a .cmd/.bat script");
            // Inside double quotes cmd treats & | < > ^ literally.
            var line = string.Join(' ', new[] { path }.Concat(args).Select(s => "\"" + s + "\""));
            return new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"{line}\"" };
        }
        if (!windows && ext.Equals(".sh", StringComparison.OrdinalIgnoreCase))
        {
            var sh = new ProcessStartInfo("/bin/sh");
            sh.ArgumentList.Add(path);
            foreach (var a in args) sh.ArgumentList.Add(a);
            return sh;
        }
        var psi = new ProcessStartInfo(path);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    static string Describe(char c) => c switch { '\r' => @"'\r'", '\n' => @"'\n'", _ => $"'{c}'" };
}
