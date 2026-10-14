using System.Text.Json;

namespace GhNotify.Core;

/// <summary>Loads config.json; on any error keeps the last good config and exposes the error.</summary>
public sealed class ConfigStore(string path)
{
    public string Path { get; } = path;
    public AppConfig Current { get; private set; } = new();
    public string? Error { get; private set; }
    /// <summary>SHA-256 of the exact bytes <see cref="Current"/> was loaded from (kept from the last good load when a load fails); null before any load.</summary>
    public string? LoadedHash { get; private set; }

    /// <summary>The last load succeeded and the file still holds exactly those bytes (so a reload would change nothing).</summary>
    public bool IsCurrent => Error is null && LoadedHash is not null && FileHash() == LoadedHash;

    public void Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(new AppConfig(), Json.Options));
                Current = new AppConfig();
                LoadedHash = FileHash();
                Error = null;
                return;
            }
            var bytes = File.ReadAllBytes(Path);
            var config = JsonSerializer.Deserialize<AppConfig>(Decode(bytes), Json.Options)
                         ?? throw new InvalidDataException("config.json is empty");
            Validate(config);
            Current = config;
            LoadedHash = Hash(bytes);
            Error = null;
        }
        catch (Exception ex)
        {
            // Any failure (bad JSON, nulls, IO, unexpected) keeps the last good config; a bad edit must never stop the app.
            Error = $"config.json: {ex.Message}";
            Log.Error(Error);
        }
    }

    public static void Validate(AppConfig c)
    {
        if (ConfigValidator.Errors(c) is [var first, ..]) throw new InvalidDataException(first.Message);
    }

    /// <summary>SHA-256 of the file's bytes, or null when it doesn't exist.</summary>
    public string? FileHash()
    {
        if (!File.Exists(Path)) return null;
        return Hash(File.ReadAllBytes(Path));
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>Decodes like File.ReadAllText (UTF-8 by default, BOMs honoured).</summary>
    static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static AppConfig Clone(AppConfig c) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c, Json.Options), Json.Options)!;

    /// <summary>
    /// Validates, refuses if the file changed since <paramref name="expectedHash"/> (null = overwrite), keeps config.json.bak,
    /// writes atomically and reloads. Throws InvalidDataException (invalid) or ConfigConflictException (changed on disk).
    /// </summary>
    public void Save(AppConfig config, string? expectedHash)
    {
        Validate(config);
        if (expectedHash is not null && FileHash() is { } now && now != expectedHash) throw new ConfigConflictException();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json.Options));
        if (File.Exists(Path)) File.Replace(tmp, Path, Path + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, Path);
        Load();
    }
}

public sealed class ConfigConflictException() : Exception("config.json changed outside Settings");
