using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Services;

internal sealed class AppSettings
{
    public PdfPageSize PageSize { get; set; } = PdfPageSize.A4;

    public PdfMargin Margin { get; set; } = PdfMargin.FiveMillimeters;

    public PdfQuality Quality { get; set; } = PdfQuality.Balanced;

    public double WindowWidth { get; set; } = 1180;

    public double WindowHeight { get; set; } = 780;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public bool IsMaximized { get; set; }
}
