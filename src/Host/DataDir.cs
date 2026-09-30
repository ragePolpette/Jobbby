using Microsoft.Extensions.Configuration;

namespace Host;

/// <summary>
/// The one directory holding a Jobbby installation's state (settings, CV, ledger, cursors,
/// runs). Its location always comes from configuration (<c>Jobbby:DataDir</c>), never from code.
/// </summary>
public sealed record DataDir(string Root)
{
    public const string ConfigurationKey = "Jobbby:DataDir";

    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string CvJsonPath => Path.Combine(Root, "cv.json");
    public string CvPdfPath => Path.Combine(Root, "cv.pdf");
    public string CvExtractedPath => Path.Combine(Root, "cv.extracted.json");
    public string ApplicationsPath => Path.Combine(Root, "applications.json");
    public string CursorsPath => Path.Combine(Root, "cursors.json");
    public string RunReportsPath => Path.Combine(Root, "run-reports.json");
    public string LockPath => Path.Combine(Root, ".jobbby.lock");
    public string SkillAliasesPath => Path.Combine(Root, "skill-aliases.json");
    public string RunsDirectory => Path.Combine(Root, "runs");

    public static DataDir FromConfiguration(IConfiguration configuration)
    {
        var root = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                $"Missing {ConfigurationKey}: set it (e.g. environment variable Jobbby__DataDir) to the directory holding settings, CV and history.");
        }

        var fullPath = Path.GetFullPath(root);
        Directory.CreateDirectory(fullPath);
        return new DataDir(fullPath);
    }
}
