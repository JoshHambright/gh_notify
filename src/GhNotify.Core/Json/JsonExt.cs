using System.Globalization;
using System.Text.Json;

namespace GhNotify.Core;

/// <summary>Null-tolerant accessors: missing or wrong-typed properties yield null/default instead of throwing.</summary>
public static class JsonExt
{
    static bool TryGet(JsonElement e, string name, out JsonElement v)
    {
        v = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v);
    }

    public static string? Str(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int? Int(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static long? Long(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i) ? i : null;

    public static bool Bool(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.True;

    public static JsonElement Obj(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static DateTimeOffset Date(this JsonElement e, string name) =>
        DateTimeOffset.TryParse(e.Str(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d : DateTimeOffset.MinValue;
}
