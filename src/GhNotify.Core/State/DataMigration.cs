namespace GhNotify.Core;

/// <summary>v1.2 moved app data out of %LocalAppData%\GhNotify (now the installer's folder). Never throws.</summary>
public static class DataMigration
{
    public static void Run(string legacyDir, string newDir)
    {
        try
        {
            var oldState = Path.Combine(legacyDir, "state.json");
            var newState = Path.Combine(newDir, "state.json");
            if (!File.Exists(oldState) || File.Exists(newState)) return;
            Directory.CreateDirectory(newDir);
            File.Move(oldState, newState);
            var oldLogs = Path.Combine(legacyDir, "logs");
            var newLogs = Path.Combine(newDir, "logs");
            if (Directory.Exists(oldLogs) && !Directory.Exists(newLogs)) Directory.Move(oldLogs, newLogs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failed move just means a fresh state file.
        }
    }
}
