using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// Guards the palette against the bug that made three settings unusable.
/// </summary>
/// <remarks>
/// <para>
/// Reported 2026-08-22: the page size, margin and quality dropdowns showed nothing,
/// though the options could still be selected. The cause was a colour mismatch - the popup was
/// painted by the framework theme while its text inherited the window's foreground, leaving black
/// text on a dark surface. He also said the app was "too bright", which was the same fault seen
/// from the other side.
/// </para>
/// <para>
/// The lesson is that <b>nothing in the build could have caught it</b>: the XAML was valid, the
/// app ran, the items were present in the automation tree and were selectable. Only looking at it
/// revealed the problem. So this test checks the property that actually matters - whether the text
/// can be READ against the surface behind it - and it does so by parsing the theme files as XML,
/// which needs no WPF, no window and no screenshot.
/// </para>
/// <para>
/// The dictionaries are parsed rather than loaded so this runs headless in CI.
/// </para>
/// </remarks>
public sealed class ThemeContrastTests
{
    private const double TextMinimum = 4.5;      // WCAG 2.2 AA, normal-sized text
    private const double ComponentMinimum = 3.0; // WCAG 2.2 AA, 1.4.11 non-text contrast

    /// <summary>Text-on-surface pairs. Each must be readable in every theme.</summary>
    public static TheoryData<string, string, string, double> ReadabilityPairs() => new()
    {
        // theme, foreground key, background key, minimum ratio
        { "Dark",  "ForegroundBrush",             "AppBackgroundBrush",     TextMinimum },
        { "Dark",  "ForegroundBrush",             "CardBackgroundBrush",    TextMinimum },
        { "Dark",  "ForegroundBrush",             "SubtleBackgroundBrush",  TextMinimum },
        { "Dark",  "ForegroundBrush",             "ControlBackgroundBrush", TextMinimum },
        // The bug, pinned. A dropdown option must be readable against its own popup.
        { "Dark",  "ForegroundBrush",             "PopupBackgroundBrush",   TextMinimum },
        { "Dark",  "MutedForegroundBrush",        "AppBackgroundBrush",     TextMinimum },
        { "Dark",  "MutedForegroundBrush",        "CardBackgroundBrush",    TextMinimum },
        { "Dark",  "ItemSelectedForegroundBrush", "ItemSelectedBrush",      TextMinimum },
        { "Dark",  "ControlBorderBrush",          "ControlBackgroundBrush", ComponentMinimum },
        { "Dark",  "ControlBorderBrush",          "AppBackgroundBrush",     ComponentMinimum },

        { "Light", "ForegroundBrush",             "AppBackgroundBrush",     TextMinimum },
        { "Light", "ForegroundBrush",             "CardBackgroundBrush",    TextMinimum },
        { "Light", "ForegroundBrush",             "SubtleBackgroundBrush",  TextMinimum },
        { "Light", "ForegroundBrush",             "ControlBackgroundBrush", TextMinimum },
        { "Light", "ForegroundBrush",             "PopupBackgroundBrush",   TextMinimum },
        { "Light", "MutedForegroundBrush",        "AppBackgroundBrush",     TextMinimum },
        { "Light", "MutedForegroundBrush",        "CardBackgroundBrush",    TextMinimum },
        { "Light", "ItemSelectedForegroundBrush", "ItemSelectedBrush",      TextMinimum },
        { "Light", "ControlBorderBrush",          "ControlBackgroundBrush", ComponentMinimum },
        { "Light", "ControlBorderBrush",          "AppBackgroundBrush",     ComponentMinimum },
    };

    [Theory]
    [MemberData(nameof(ReadabilityPairs))]
    public void Text_is_readable_against_the_surface_behind_it(
        string theme, string foregroundKey, string backgroundKey, double minimum)
    {
        var palette = LoadPalette(theme);

        Assert.True(palette.ContainsKey(foregroundKey), $"{theme}.xaml is missing {foregroundKey}");
        Assert.True(palette.ContainsKey(backgroundKey), $"{theme}.xaml is missing {backgroundKey}");

        var ratio = ContrastRatio(palette[foregroundKey], palette[backgroundKey]);

        Assert.True(
            ratio >= minimum,
            $"{theme}: {foregroundKey} on {backgroundKey} is {ratio:F2}:1, needs {minimum:F1}:1. " +
            "This is the failure that made the dropdown options invisible.");
    }

    /// <summary>
    /// The two dictionaries are swapped wholesale at runtime, so a key in one and not the other is
    /// a missing-resource crash the moment somebody switches theme.
    /// </summary>
    [Fact]
    public void Both_themes_define_exactly_the_same_keys()
    {
        var dark = LoadPalette("Dark").Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var light = LoadPalette("Light").Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(dark, light);
    }

    /// <summary>
    /// A screen reader takes a ComboBoxItem's name from its content, and the content here is an
    /// <c>OptionChoice</c> record. Without an explicit AutomationProperties.Name it announces the
    /// raw record: "OptionChoice { Value = A4, Label = ..., Description = ... }".
    ///
    /// Found 2026-08-24 while verifying the visible dropdown fix. The window looked completely
    /// correct and the contrast was fine; only the accessibility tree showed it. A screenshot
    /// could never have caught this, which is exactly why it is worth a structural guard.
    /// </summary>
    [Fact]
    public void Combo_box_items_expose_their_label_to_assistive_technology()
    {
        var appXaml = Path.Combine(RepositoryRoot(), "src", "LocalPhotoPDF", "App.xaml");
        var text = File.ReadAllText(appXaml);

        var styleStart = text.IndexOf("x:Key=\"ComboBoxItemStyle\"", StringComparison.Ordinal);
        Assert.True(styleStart >= 0, "ComboBoxItemStyle is gone from App.xaml.");

        var styleEnd = text.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleEnd > styleStart, "ComboBoxItemStyle is not closed.");

        var style = text[styleStart..styleEnd];

        Assert.Contains("AutomationProperties.Name", style, StringComparison.Ordinal);
        Assert.Contains("{Binding Label}", style, StringComparison.Ordinal);
    }

    /// <summary>
    /// The app must never reintroduce legacy <c>SystemColors.*</c> brushes. Mixing them with the
    /// framework theme is precisely what produced the invisible dropdown, because the two colour
    /// systems do not agree and neither one is in charge.
    /// </summary>
    [Fact]
    public void No_xaml_uses_legacy_system_colours()
    {
        var appDirectory = Path.Combine(RepositoryRoot(), "src", "LocalPhotoPDF");
        var offenders = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path)
                .Contains("x:Static SystemColors.", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(appDirectory, path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These files reintroduce legacy SystemColors brushes, which do not follow the app " +
            "theme and will produce unreadable controls: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Negative control: proves this check can actually FAIL.
    /// </summary>
    /// <remarks>
    /// A guard that has only ever returned "pass" is not evidence of anything - it may be
    /// measuring the wrong thing, or nothing at all. So the suite reproduces the original defect
    /// and requires it to be rejected: black text (the legacy <c>SystemColors.WindowTextBrushKey</c>
    /// the window used to inherit) against the dark popup the framework theme actually drew.
    /// If this test ever passes, the contrast calculation above has stopped working and every
    /// other result in this file is worthless.
    /// </remarks>
    [Fact]
    public void The_original_bug_would_be_rejected()
    {
        const string LegacyWindowText = "#000000"; // SystemColors.WindowTextBrushKey, always black
        const string FrameworkDarkPopup = "#2B2B2B"; // what the dark Fluent popup actually painted

        var ratio = ContrastRatio(LegacyWindowText, FrameworkDarkPopup);

        Assert.True(
            ratio < TextMinimum,
            $"The known-bad combination scored {ratio:F2}:1, which would have PASSED. " +
            "The contrast calculation is broken and this whole test class is meaningless.");
    }

    private static Dictionary<string, string> LoadPalette(string theme)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "LocalPhotoPDF", "Themes", theme + ".xaml");
        Assert.True(File.Exists(path), $"Theme file not found: {path}");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!
            .Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                e => (string)e.Attribute(x + "Key")!,
                e => (string)e.Attribute("Color")!,
                StringComparer.Ordinal);
    }

    /// <summary>Walks up from the test binaries until the solution file appears.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "LocalPhotoPDF.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>WCAG 2.2 relative luminance, from the sRGB definition.</summary>
    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        // Tolerate #AARRGGBB as well as #RRGGBB; alpha is not part of the calculation.
        if (value.Length == 8)
        {
            value = value[2..];
        }

        var channels = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var component =
                int.Parse(value.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                / 255.0;
            channels[i] = component <= 0.04045
                ? component / 12.92
                : Math.Pow((component + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
    }

    private static double ContrastRatio(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
