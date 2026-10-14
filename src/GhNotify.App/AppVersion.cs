using System.Reflection;

namespace GhNotify.App;

public static class AppVersion
{
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0-dev";
}
