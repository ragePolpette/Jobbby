namespace Host;

/// <summary>
/// First-start migration of files older versions kept next to the executable. The ledger
/// and run reports are copied (originals stay); cursors are not migrated because their keys
/// now include country and area, so old ones would never match. Existing DataDir files are
/// never overwritten.
/// </summary>
public static class LegacyMigration
{
    public static IReadOnlyList<string> Run(DataDir dataDir, IEnumerable<string> legacyDirectories)
    {
        var directories = legacyDirectories.Where(Directory.Exists).ToList();
        var notes = new List<string>();

        foreach (var (fileName, target) in new[]
                 {
                     ("applications.json", dataDir.ApplicationsPath),
                     ("run-reports.json", dataDir.RunReportsPath),
                 })
        {
            if (File.Exists(target))
                continue;

            var source = directories.Select(directory => Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);
            if (source is null)
                continue;

            File.Copy(source, target);
            notes.Add($"Migrato {fileName} da {Path.GetDirectoryName(source)}.");
        }

        return notes;
    }
}
