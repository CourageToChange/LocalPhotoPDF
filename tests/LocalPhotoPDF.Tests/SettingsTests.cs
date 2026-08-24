using System.IO;
using LocalPhotoPDF.Services;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// Covers the settings behaviour a user would notice, without needing a window.
/// </summary>
public sealed class SettingsTests
{
    private static string Documents =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Matches how SettingsService serialises, so the round trip is realistic.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public void Unset_default_folder_falls_back_to_documents()
    {
        var settings = new AppSettings();

        Assert.Equal(Documents, SettingsService.ResolveOutputDirectory(settings));
    }

    [Fact]
    public void Null_settings_still_produce_a_usable_folder()
    {
        // Defensive: a caller with no settings loaded must not hand null to a file dialog.
        Assert.Equal(Documents, SettingsService.ResolveOutputDirectory(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_default_folder_falls_back_to_documents(string configured)
    {
        var settings = new AppSettings { DefaultOutputDirectory = configured };

        Assert.Equal(Documents, SettingsService.ResolveOutputDirectory(settings));
    }

    /// <summary>
    /// The case that matters most, and the reason this is resolved at use time rather than cached.
    /// </summary>
    /// <remarks>
    /// A folder chosen months ago can be deleted, renamed, or sit on an external drive that is not
    /// plugged in. Handing that dead path to a file dialog does not raise an error - the dialog
    /// quietly opens somewhere else, which users tend to read as the app losing their setting.
    /// </remarks>
    [Fact]
    public void Vanished_default_folder_falls_back_to_documents()
    {
        var vanished = Path.Combine(Path.GetTempPath(), "LocalPhotoPDF-gone-" + Guid.NewGuid());
        var settings = new AppSettings { DefaultOutputDirectory = vanished };

        Assert.False(Directory.Exists(vanished));
        Assert.Equal(Documents, SettingsService.ResolveOutputDirectory(settings));
    }

    [Fact]
    public void An_existing_default_folder_is_used()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LocalPhotoPDF-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var settings = new AppSettings { DefaultOutputDirectory = folder };

            Assert.Equal(folder, SettingsService.ResolveOutputDirectory(settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Dark on first run is deliberate: a brand new install must not depend on a Windows
    /// setting being right to be comfortable to look at.
    /// </summary>
    [Fact]
    public void A_fresh_install_defaults_to_dark()
    {
        Assert.Equal(AppTheme.Dark, new AppSettings().Theme);
    }

    /// <summary>
    /// An upgraded settings file written before these fields existed must keep working, and must
    /// behave exactly as the old build did: Documents, and no folder popping open after export.
    /// </summary>
    [Fact]
    public void Settings_written_by_an_older_version_still_load()
    {
        var older = """
            {
              "PageSize": "A4",
              "Margin": "FiveMillimeters",
              "Quality": "Balanced",
              "WindowWidth": 1180,
              "WindowHeight": 780
            }
            """;

        var path = Path.Combine(Path.GetTempPath(), "LocalPhotoPDF-old-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, older);
        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(path), SerializerOptions);

            Assert.NotNull(settings);
            Assert.Equal(AppTheme.Dark, settings!.Theme);
            Assert.Null(settings.DefaultOutputDirectory);
            Assert.False(settings.OpenFolderAfterExport);
            Assert.Equal(Documents, SettingsService.ResolveOutputDirectory(settings));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
