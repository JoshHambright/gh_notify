namespace GhNotify.App.Mac;

/// <summary>Single instance: an exclusive lock on a file in the data folder (the Windows host uses a named mutex). The OS drops the
/// lock when the process exits, even after a crash.</summary>
public sealed class InstanceLock : IDisposable
{
    readonly FileStream _stream;

    InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>The lock, or null when another gh_notify holds it.</summary>
    public static InstanceLock? TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new InstanceLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
