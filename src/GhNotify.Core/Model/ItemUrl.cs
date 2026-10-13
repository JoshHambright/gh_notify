namespace GhNotify.Core;

public static class ItemUrl
{
    /// <summary>Item URLs come from API data and are shell-executed when clicked, so only absolute https URLs are opened.</summary>
    public static bool IsOpenable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
