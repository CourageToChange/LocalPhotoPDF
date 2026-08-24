using System.Windows;
using Microsoft.Win32;

namespace LocalPhotoPDF.Services;

/// <summary>
/// Swaps the app's colour palette, and keeps the framework's own chrome in step with it.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because of a real bug. Before it, <c>App.xaml</c> declared
/// <c>ThemeMode="System"</c> and then painted every surface with legacy
/// <c>SystemColors.*BrushKey</c> brushes. Those two systems are independent: the framework theme
/// follows Windows, while <c>SystemColors.*</c> returns classic Win32 colours that are effectively
/// always light. On a machine set to dark that produced two faults from one cause - the window
/// forced itself bright, and the ComboBox popup kept a dark framework background while its items
/// inherited the window's black foreground, leaving the options invisible but still selectable.
/// </para>
/// <para>
/// The rule this class enforces: <b>one palette decides everything, and the framework chrome is
/// told to match it.</b> Setting <see cref="Application.ThemeMode"/> alongside the dictionary swap
/// matters - it governs the title bar, scrollbars and other native chrome that our brushes never
/// touch. Change one without the other and the mismatch comes straight back.
/// </para>
/// </remarks>
internal static class ThemeService
{
    private const string DarkDictionary = "Themes/Dark.xaml";
    private const string LightDictionary = "Themes/Light.xaml";

    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Applies <paramref name="theme"/> to the running application.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        var dark = ResolveIsDark(theme);
        var source = new Uri(dark ? DarkDictionary : LightDictionary, UriKind.Relative);

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var merged = application.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = source };

        // Replace the palette in place rather than clearing and re-adding. Every brush is
        // referenced with DynamicResource, so live windows repaint themselves; clearing first
        // would briefly leave those lookups unresolved.
        var existing = merged.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)));

        if (existing is null)
        {
            merged.Insert(0, replacement);
        }
        else
        {
            merged[merged.IndexOf(existing)] = replacement;
        }

        // Keep the native chrome in step. This is the half that caused the original bug.
        //
        // WPF0001 is suppressed deliberately and as narrowly as possible. ThemeMode is still
        // marked experimental in .NET 10, but it is the only way to tell WPF what to paint the
        // title bar and scrollbars - the surfaces our own brushes cannot reach. Leaving it unset
        // is what let the framework and the palette disagree in the first place. If a future SDK
        // renames or removes it, this single line is the only thing that needs revisiting.
#pragma warning disable WPF0001
        application.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
    }

    /// <summary>
    /// Turns a preference into a concrete answer: is the app dark right now?
    /// </summary>
    private static bool ResolveIsDark(AppTheme theme) => theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsWindowsInDarkMode(),
    };

    /// <summary>
    /// Reads the Windows app theme. Defaults to dark when the value cannot be read, matching the
    /// app's own default rather than silently flipping the user into a bright window.
    /// </summary>
    private static bool IsWindowsInDarkMode()
    {
        try
        {
            // 0 = dark, 1 = light. Note this is AppsUseLightTheme, not SystemUsesLightTheme:
            // the latter controls the taskbar, not application windows.
            var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return value is int light && light == 0;
        }
        catch (Exception)
        {
            // A missing or unreadable key is not worth failing startup over.
            return true;
        }
    }
}
