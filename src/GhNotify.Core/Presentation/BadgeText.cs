namespace GhNotify.Core;

public static class BadgeText
{
    public static string? For(int count) => count <= 0 ? null : count > 99 ? "99+" : count.ToString();
}
