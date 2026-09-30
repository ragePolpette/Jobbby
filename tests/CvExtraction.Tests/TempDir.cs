namespace CvExtraction.Tests;

/// <summary>A throwaway directory under the system temp path, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jobbby-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
