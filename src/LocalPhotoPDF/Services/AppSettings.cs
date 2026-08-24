using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Services;

/// <summary>
/// Which palette the app paints itself with.
/// </summary>
/// <remarks>
/// Serialised by name (the service uses <c>JsonStringEnumConverter</c>), so these members can be
/// reordered safely but must not be renamed without breaking existing settings files.
/// </remarks>
internal enum AppTheme
{
    /// <summary>Follow whatever Windows is set to.</summary>
    System,

    Light,

    /// <summary>The default, chosen deliberately on 2026-08-22.</summary>
    Dark,
}

internal sealed class AppSettings
{
    public PdfPageSize PageSize { get; set; } = PdfPageSize.A4;

    public PdfMargin Margin { get; set; } = PdfMargin.FiveMillimeters;

    public PdfQuality Quality { get; set; } = PdfQuality.Balanced;

    /// <summary>
    /// Dark unless the user says otherwise. This is a deliberate default rather than "System",
    /// because a fresh install should not depend on a Windows setting being right to be
    /// comfortable to look at.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>
    /// Where the Save dialog opens. Null means "use Documents", which is what the app did before
    /// this setting existed, so an upgraded settings file behaves exactly as it used to.
    /// </summary>
    /// <remarks>
    /// Never trusted blindly at use time - the folder can be deleted, renamed, or live on a drive
    /// that is not plugged in. Callers fall back to Documents when it no longer exists.
    /// </remarks>
    public string? DefaultOutputDirectory { get; set; }

    /// <summary>
    /// After a successful export, open the containing folder in Explorer.
    /// </summary>
    public bool OpenFolderAfterExport { get; set; }

    public double WindowWidth { get; set; } = 1180;

    public double WindowHeight { get; set; } = 780;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public bool IsMaximized { get; set; }
}
