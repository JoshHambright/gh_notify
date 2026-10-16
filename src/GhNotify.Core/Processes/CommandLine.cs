using System.Text;

namespace GhNotify.Core;

public static class CommandLine
{
    /// <summary>Splits a command line on spaces; a "double quoted" section is one token (quotes removed).</summary>
    public static List<string> Split(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in command)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (c == ' ' && !quoted)
            {
                if (any) tokens.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else { current.Append(c); any = true; }
        }
        if (any) tokens.Add(current.ToString());
        return tokens;
    }
}
