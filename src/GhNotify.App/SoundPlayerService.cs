using System.Collections.Concurrent;
using System.Media;
using System.Windows;
using GhNotify.Core;

namespace GhNotify.App;

public static class SoundPlayerService
{
    static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plays "builtin:&lt;name&gt;" or a .wav path asynchronously. False (and one log line per path) when it can't.</summary>
    public static bool Play(string sound)
    {
        try
        {
            if (sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            {
                var name = sound["builtin:".Length..].ToLowerInvariant();
                if (!SoundNames.BuiltIn.Contains(name)) return false;
                var res = Application.GetResourceStream(new Uri($"pack://application:,,,/GhNotify;component/Sounds/{name}.wav"));
                if (res is null) return false;
                new SoundPlayer(res.Stream).Play();
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

    /// <summary>Plays the sound, falling back to the system notification sound.</summary>
    public static void PlayOrDefault(string sound)
    {
        if (!Play(sound)) SystemSounds.Asterisk.Play();
    }
}
