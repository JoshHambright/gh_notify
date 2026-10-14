namespace GhNotify.Core;

public enum MuteSaveResult { Saved, ConfigError, CommentsNotAcknowledged, Failed }

/// <summary>Edits filters.mute in config.json with the same guards as the Settings window (error, conflict, comments).</summary>
public static class MuteConfigEditor
{
    public static MuteSaveResult TrySave(ConfigStore config, Action<List<string>> edit, bool commentsWarningShown)
    {
        if (config.Error is not null) return MuteSaveResult.ConfigError;
        try
        {
            if (!commentsWarningShown && File.Exists(config.Path) && SettingsDraft.HasComments(File.ReadAllText(config.Path)))
                return MuteSaveResult.CommentsNotAcknowledged;
            var next = ConfigStore.Clone(config.Current);
            edit(next.Filters.Mute);
            config.Save(next, config.LoadedHash);
            return MuteSaveResult.Saved;
        }
        catch (Exception ex) when (ex is ConfigConflictException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("could not save mute", ex);
            return MuteSaveResult.Failed;
        }
    }
}
