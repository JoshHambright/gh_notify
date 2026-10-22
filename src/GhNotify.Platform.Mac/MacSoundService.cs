using System.Collections.Concurrent;
using System.Reflection;
using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>Plays sounds with afplay. The built-in wavs are written once to the cache folder; anything unplayable ("default", an
/// unknown built-in, a missing file) plays the system Ping instead.</summary>
public sealed class MacSoundService(IProcessRunner runner, string cacheDir) : ISoundService
{
    internal const string Fallback = "/System/Library/Sounds/Ping.aiff";
    static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);

    public void Play(string soundIdOrPath)
    {
        try { runner.StartDetached(new ProcessSpec("afplay", [Resolve(soundIdOrPath) ?? Fallback])); }
        catch (Exception ex)
        {
            if (Warned.TryAdd(soundIdOrPath, 0)) Log.Error($"could not play sound '{soundIdOrPath}'", ex);
        }
    }

    /// <summary>The file to play, or null for the fallback.</summary>
    internal string? Resolve(string sound)
    {
        if (sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            var name = sound["builtin:".Length..].ToLowerInvariant();
            return SoundNames.BuiltIn.Contains(name) ? Extract(name) : null;
        }
        return File.Exists(sound) ? sound : null;
    }

    string? Extract(string name)
    {
        var path = Path.Combine(cacheDir, name + ".wav");
        if (File.Exists(path)) return path;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sounds.{name}.wav");
        if (stream is null) return null;
        Directory.CreateDirectory(cacheDir);
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp)) stream.CopyTo(file);
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
