namespace GhNotify.Core;

public static class Log
{
    static readonly object Gate = new();
    static string? _dir;

    public static void Configure(string dir)
    {
        Directory.CreateDirectory(dir);
        _dir = dir;
        foreach (var f in Directory.GetFiles(dir, "*.log"))
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7))
                try { File.Delete(f); } catch (IOException) { }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        if (_dir is null) return;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTime.Now:HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
    }
}
