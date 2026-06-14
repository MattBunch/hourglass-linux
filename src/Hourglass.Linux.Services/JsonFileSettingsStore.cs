namespace Hourglass.Linux.Services;

using System.Text.Json;
using Hourglass.Platform;

public sealed class JsonFileSettingsStore : ISettingsStore
{
    private const string FileExtension = ".json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true
    };

    private readonly ISettingsPathService settingsPathService;

    public JsonFileSettingsStore(ISettingsPathService settingsPathService)
    {
        this.settingsPathService = settingsPathService ?? throw new ArgumentNullException(nameof(settingsPathService));
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        string path = this.GetSettingsPath(key);

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return default;
        }
        catch (DirectoryNotFoundException)
        {
            return default;
        }
        catch (JsonException)
        {
            return default;
        }
        catch (NotSupportedException)
        {
            return default;
        }
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        string path = this.GetSettingsPath(key);
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Settings path must include a directory.");
        Directory.CreateDirectory(directory);

        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string GetSettingsPath(string key)
    {
        string fileName = GetSettingsFileName(key);
        return Path.Combine(this.settingsPathService.GetSettingsDirectory(), fileName);
    }

    internal static string GetSettingsFileName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Settings key must not be empty.", nameof(key));
        }

        foreach (char character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')
            {
                throw new ArgumentException("Settings key may only contain ASCII letters, digits, dots, dashes, and underscores.", nameof(key));
            }
        }

        return $"{key}{FileExtension}";
    }
}
