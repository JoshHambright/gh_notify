using System.Collections.Concurrent;
using System.Media;
using System.Reflection;
using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Windows;

public sealed class WinSoundService : ISoundService
{
    static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plays the sound, falling back to the system notification sound.</summary>
    public void Play(string soundIdOrPath)
    {
        if (!TryPlay(soundIdOrPath)) SystemSounds.Asterisk.Play();
    }

    /// <summary>Plays "builtin:&lt;name&gt;" or a .wav path asynchronously. False (and one log line per path) when it can't.</summary>
    static bool TryPlay(string sound)
    {
        try
        {
            if (sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            {
                var name = sound["builtin:".Length..].ToLowerInvariant();
                if (!SoundNames.BuiltIn.Contains(name)) return false;
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Sounds.{name}.wav");
                if (stream is null) return false;
                new SoundPlayer(stream).Play();
                return true;
            }
            if (!File.Exists(sound)) throw new FileNotFoundException(sound);
            new SoundPlayer(sound).Play();
            return true;
        }
        catch (Exception ex)
        {
            if (Warned.TryAdd(sound, 0)) Log.Error($"could not play sound '{sound}'", ex);
            return false;
        }
    }
}
