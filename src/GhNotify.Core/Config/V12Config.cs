namespace GhNotify.Core;

public sealed class UpdatesConfig
{
    public bool Enabled { get; set; } = true;
    public int CheckHours { get; set; } = 4;
}

public sealed class NotificationsConfig
{
    public List<SoundRule> Sounds { get; set; } = [];
}

public sealed class SoundRule
{
    public ItemMatch? Match { get; set; }
    /// <summary>"default" | "none" | "builtin:&lt;name&gt;" | absolute path to a .wav file.</summary>
    public string Sound { get; set; } = "default";
    public bool AlertOffHours { get; set; }
}

public sealed class FiltersConfig
{
    public List<string> Mute { get; set; } = [];
    public List<string> Hide { get; set; } = [];
}

public static class SoundNames
{
    public static readonly IReadOnlyList<string> BuiltIn = ["chime", "ping", "alert", "alarm", "soft"];
}
