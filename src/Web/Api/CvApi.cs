using Host;

namespace Web.Api;

public static class CvApi
{
    public static void MapCvApi(this WebApplication app)
    {
        // Read-only here: which CV file a run will use. Upload and editing come with the CV page.
        app.MapGet("/api/cv", (DataDir dataDir, IConfiguration configuration) =>
        {
            var path = CvLocator.Find(dataDir, configuration);
            return path is null
                ? Results.Json(new { file = (string?)null, message = CvLocator.NotFoundMessage(dataDir) })
                : Results.Json(new { file = Path.GetFileName(path), updatedAt = File.GetLastWriteTimeUtc(path) });
        });
    }
}
