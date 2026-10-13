using System.Diagnostics;

namespace GhNotify.Core;

/// <summary>Resolves a command on PATH (.exe/.cmd/.bat) and builds a ProcessStartInfo; .cmd/.bat run via cmd.exe.</summary>
public static class CommandResolver
{
    public static string? Find(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        string[] exts = Path.HasExtension(name) ? [""] : [".exe", ".cmd", ".bat"];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in dirs)
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string name, IReadOnlyList<string> args)
    {
        var path = Find(name) ?? throw new FileNotFoundException($"'{name}' was not found on PATH", name);
        var ext = Path.GetExtension(path);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // cmd /s /c "<whole line>": /s strips only the outer quotes, so quoted paths and args survive.
            var line = string.Join(' ', new[] { path }.Concat(args).Select(Quote));
            return new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"{line}\"" };
        }
        var psi = new ProcessStartInfo(path);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    static string Quote(string s) =>
        s.Length > 0 && s.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^']) < 0 ? s : "\"" + s.Replace("\"", "\\\"") + "\"";
}
