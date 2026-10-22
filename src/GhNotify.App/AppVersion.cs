using System.Reflection;

namespace GhNotify.App;

public static class AppVersion
{
    public static string Current => Informational?.Split('+')[0] ?? "0.0.0-dev";

    /// <summary>The git commit the build came from (the SDK appends "+sha" to the informational version), or "" when unknown.</summary>
    public static string Commit => Informational?.Split('+') is [_, var sha, ..] ? sha : "";

    static string? Informational =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
