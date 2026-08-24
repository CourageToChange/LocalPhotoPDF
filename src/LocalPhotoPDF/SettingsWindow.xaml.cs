using System.IO;
using System.Windows;
using LocalPhotoPDF.Services;
using Microsoft.Win32;

namespace LocalPhotoPDF;

/// <summary>
/// User preferences: appearance, and where new PDFs are saved.
/// </summary>
/// <remarks>
/// Edits a COPY of the settings and only writes them back when Save is pressed, so Cancel really
/// cancels - including the theme, which is previewed live and reverted if the user backs out.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly AppTheme _themeOnOpen;

    private string? _pendingFolder;

    internal SettingsWindow(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _themeOnOpen = settings.Theme;

        InitializeComponent();

        ThemeCombo.ItemsSource = new[]
        {
            new ThemeChoice(AppTheme.Dark, "Dark"),
            new ThemeChoice(AppTheme.Light, "Light"),
            new ThemeChoice(AppTheme.System, "Match Windows"),
        };
        // ⚠️ Deliberately NOT setting DisplayMemberPath. The application-level ComboBox style
        // already supplies an ItemTemplate, and WPF throws if both are set - which it did, from
        // this constructor, so the dialog silently never opened. The shared template binds to
        // "Label", which is why ThemeChoice names its display property that.
        ThemeCombo.SelectedValuePath = nameof(ThemeChoice.Value);
        ThemeCombo.SelectedValue = settings.Theme;
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;

        _pendingFolder = settings.DefaultOutputDirectory;
        OpenFolderCheck.IsChecked = settings.OpenFolderAfterExport;
        UpdateFolderBox();
    }

    private void UpdateFolderBox()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(_pendingFolder))
        {
            FolderBox.Text = documents + "  (default)";
            return;
        }

        FolderBox.Text = Directory.Exists(_pendingFolder)
            ? _pendingFolder
            // Say so rather than silently showing a path that will not be used.
            : _pendingFolder + "  (missing — Documents will be used)";
    }

    private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Preview immediately: choosing a theme you cannot see until you commit is a poor trade.
        if (ThemeCombo.SelectedValue is AppTheme theme)
        {
            ThemeService.Apply(theme);
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where new PDFs are saved",
            InitialDirectory = SettingsService.ResolveOutputDirectory(_settings),
        };

        if (dialog.ShowDialog(this) == true)
        {
            _pendingFolder = dialog.FolderName;
            UpdateFolderBox();
        }
    }

    private void ResetFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingFolder = null;
        UpdateFolderBox();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeCombo.SelectedValue is AppTheme theme)
        {
            _settings.Theme = theme;
        }

        // Store null rather than a dead path, so the fallback is a deliberate state and not an
        // accident that has to be re-checked everywhere.
        _settings.DefaultOutputDirectory =
            string.IsNullOrWhiteSpace(_pendingFolder) || !Directory.Exists(_pendingFolder)
                ? null
                : _pendingFolder;

        _settings.OpenFolderAfterExport = OpenFolderCheck.IsChecked == true;

        _settingsService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Undo the live theme preview. Without this, Cancel would leave the app in a theme the
        // user explicitly backed out of, and the settings file would disagree with the window.
        if (_settings.Theme != _themeOnOpen)
        {
            _settings.Theme = _themeOnOpen;
        }

        ThemeService.Apply(_themeOnOpen);
        DialogResult = false;
        Close();
    }

    private sealed record ThemeChoice(AppTheme Value, string Label);
}
