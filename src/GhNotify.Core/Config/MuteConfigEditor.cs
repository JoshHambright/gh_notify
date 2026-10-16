namespace GhNotify.Core;

public enum MuteSaveResult { Saved, ConfigError, CommentsNotAcknowledged, Failed }

public enum FilterList { Mute, Hide }

/// <summary>Edits filters.mute or filters.hide in config.json with the same guards as the Settings window (error, conflict, comments).</summary>
public static class MuteConfigEditor
{
    public static MuteSaveResult TrySave(ConfigStore config, Action<List<string>> edit, bool commentsWarningShown, FilterList list = FilterList.Mute)
    {
        if (config.Error is not null) return MuteSaveResult.ConfigError;
        try
        {
            if (!commentsWarningShown && File.Exists(config.Path) && SettingsDraft.HasComments(File.ReadAllText(config.Path)))
                return MuteSaveResult.CommentsNotAcknowledged;
            var next = ConfigStore.Clone(config.Current);
            edit(list == FilterList.Hide ? next.Filters.Hide : next.Filters.Mute);
            config.Save(next, config.LoadedHash);
            return MuteSaveResult.Saved;
        }
        catch (Exception ex) when (ex is ConfigConflictException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"could not save filters.{list.ToString().ToLowerInvariant()}", ex);
            return MuteSaveResult.Failed;
        }
    }
}
