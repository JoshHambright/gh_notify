namespace GhNotify.Core;

public sealed class DisplayConfig
{
    /// <summary>Prefix ("org" or "org/project") → short name, applied to repo names in the popup.</summary>
    public Dictionary<string, string> RepoAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Repo names longer than this are middle-ellipsized (the repo segment is kept).</summary>
    public int MaxRepoChars { get; set; } = 34;
    /// <summary>macOS only: "terminal" (Terminal.app), "iterm" (iTerm2), or a command run with the working directory and the command line as its two arguments.</summary>
    public string Terminal { get; set; } = DefaultTerminal;
    public const string DefaultTerminal = "terminal";
}
