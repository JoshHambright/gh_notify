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
            if (!Directory.Exists(oldLogs)) return;
            if (!Directory.Exists(newLogs)) { Directory.Move(oldLogs, newLogs); return; }
            // Install hooks may have created the new logs dir first: merge, never overwrite.
            foreach (var file in Directory.EnumerateFiles(oldLogs))
            {
                var target = Path.Combine(newLogs, Path.GetFileName(file));
                if (!File.Exists(target)) File.Move(file, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failed move just means a fresh state file.
        }
    }
}
