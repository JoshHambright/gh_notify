namespace GhNotify.Core;

public static class DetailList
{
    /// <summary>Builds an ordered detail list, skipping null/blank values.</summary>
    public static IReadOnlyList<ItemDetail> Of(params (string Label, string? Value)[] entries) =>
        entries.Where(e => !string.IsNullOrWhiteSpace(e.Value)).Select(e => new ItemDetail(e.Label, e.Value!)).ToList();

    public static string? Short(string? sha) => sha is { Length: > 7 } ? sha[..7] : sha;

    public static string? Local(DateTimeOffset d) => d == DateTimeOffset.MinValue ? null : d.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static string StripHeads(string? refName) =>
        refName is not null && refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName ?? "";
}
