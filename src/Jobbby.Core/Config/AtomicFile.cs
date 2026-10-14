namespace Config;

/// <summary>
/// Writes a file via a temporary sibling plus rename, so a crash mid-write leaves either the
/// old content or the new one, never a truncated file.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content) =>
        Write(path, temporary => File.WriteAllText(temporary, content));

    public static void WriteAllBytes(string path, byte[] content) =>
        Write(path, temporary => File.WriteAllBytes(temporary, content));

    private static void Write(string path, Action<string> writeTemporary)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            writeTemporary(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
