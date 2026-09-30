namespace Host;

public sealed class DataDirLockedException(string message, Exception innerException) : Exception(message, innerException);

/// <summary>
/// Guarantees a single Jobbby process (CLI or web) per DataDir: the lock file stays open with
/// <see cref="FileShare.None"/> for the process lifetime, so the operating system releases it
/// even after a crash. Supported inside the container only (see the spec's lock section).
/// </summary>
public sealed class DataDirLock : IDisposable
{
    private readonly FileStream _stream;

    private DataDirLock(FileStream stream) => _stream = stream;

    public static DataDirLock Acquire(DataDir dataDir)
    {
        Directory.CreateDirectory(dataDir.Root);
        try
        {
            return new DataDirLock(new FileStream(dataDir.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex)
        {
            throw new DataDirLockedException(
                $"DataDir già in uso da un altro processo Jobbby (probabilmente la UI web): chiudilo o usa la UI. DataDir: {dataDir.Root}", ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
