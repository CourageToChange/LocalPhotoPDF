using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalPhotoPDF.Services;

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalPhotoPDF");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// The folder the Save dialog should open in, falling back to Documents.
    /// </summary>
    /// <remarks>
    /// Resolved at the moment of use, never cached. A stored folder can be deleted, renamed, or
    /// live on a drive that is not plugged in, so "it existed when the user picked it" is not a
    /// guarantee that it exists now. Handing a dead path to a file dialog makes it open somewhere
    /// unpredictable instead of failing, which is the kind of fault users blame on themselves.
    /// </remarks>
    public static string ResolveOutputDirectory(AppSettings? settings)
    {
        var configured = settings?.DefaultOutputDirectory;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch (IOException)
        {
            // Preferences are optional and must never interrupt the user's work.
        }
        catch (UnauthorizedAccessException)
        {
            // Preferences are optional and must never interrupt the user's work.
        }
    }
}
