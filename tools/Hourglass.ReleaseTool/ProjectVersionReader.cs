using System.Diagnostics;

namespace Hourglass.ReleaseTool;

internal interface IProjectVersionReader
{
    Task<ProjectVersionReadResult> ReadAsync(string projectPath);
}

internal sealed record ProjectVersionReadResult(string? Version, string? Error)
{
    public bool IsSuccess => Version != null;

    public static ProjectVersionReadResult Success(string version) => new(version, null);

    public static ProjectVersionReadResult Failure(string error) => new(null, error);
}

internal sealed class ProjectVersionReader : IProjectVersionReader
{
    public async Task<ProjectVersionReadResult> ReadAsync(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return ProjectVersionReadResult.Failure($"Project file not found: {projectPath}");
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-getProperty:Version");
        startInfo.ArgumentList.Add("-property:Configuration=Release");
        startInfo.ArgumentList.Add("-nologo");

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            string standardOutput = (await standardOutputTask.ConfigureAwait(false)).Trim();
            string standardError = (await standardErrorTask.ConfigureAwait(false)).Trim();

            if (process.ExitCode != 0)
            {
                string details = string.IsNullOrEmpty(standardError) ? standardOutput : standardError;
                return ProjectVersionReadResult.Failure($"dotnet msbuild failed for {projectPath}: {details}");
            }

            if (string.IsNullOrEmpty(standardOutput))
            {
                return ProjectVersionReadResult.Failure($"Expected a non-empty evaluated Version property in {projectPath}");
            }

            return ProjectVersionReadResult.Success(standardOutput);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ProjectVersionReadResult.Failure($"Could not run dotnet msbuild for {projectPath}: {exception.Message}");
        }
    }
}
