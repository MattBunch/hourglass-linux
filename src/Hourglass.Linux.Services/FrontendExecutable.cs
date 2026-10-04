namespace Hourglass.Linux.Services;

public static class FrontendExecutable
{
    public static string Resolve(string baseDirectory, string executable, string project, string payload)
    {
        string packaged = Path.GetFullPath(Path.Combine(baseDirectory, "..", payload, executable));
        if (File.Exists(packaged)) { return packaged; }
        string sibling = Path.Combine(baseDirectory, executable);
        if (File.Exists(sibling)) { return sibling; }
        string configuration = new DirectoryInfo(baseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Release";
        string development = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", project, "bin", configuration, "net10.0", executable));
        return File.Exists(development) ? development : executable;
    }
}
