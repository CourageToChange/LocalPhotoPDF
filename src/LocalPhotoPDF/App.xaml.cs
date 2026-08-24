using System.Windows;
using System.Windows.Threading;
using LocalPhotoPDF.Core;
using LocalPhotoPDF.Services;
using LocalPhotoPDF.ViewModels;

namespace LocalPhotoPDF;

public partial class App : Application, IDisposable
{
    private const string AppMutexName = @"Local\LocalPhotoPDF.AppMutex.v1";
    private Mutex? _appMutex;
    private bool _ownsAppMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _appMutex = new Mutex(false, AppMutexName);
        try
        {
            _ownsAppMutex = _appMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsAppMutex = true;
        }

        if (!_ownsAppMutex)
        {
            MessageBox.Show(
                "LocalPhotoPDF is already open, or setup is updating it. Close the other window and try again.",
                "LocalPhotoPDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        // Apply the palette before any window exists, so the very first paint is already correct
        // and the user never sees a flash of the wrong theme.
        ThemeService.Apply(settings.Theme);

        var viewModel = new MainViewModel(
            new WicImageImportService(),
            new PdfGenerationService(),
            new ShellService(),
            settingsService,
            settings);

        var window = new MainWindow(viewModel, settingsService, settings);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_ownsAppMutex)
        {
            _appMutex?.ReleaseMutex();
            _ownsAppMutex = false;
        }

        _appMutex?.Dispose();
        _appMutex = null;
        LocalPhotoPDF.Core.ImagingRuntime.Shutdown();
        GC.SuppressFinalize(this);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "LocalPhotoPDF ran into an unexpected problem. Your photos were not changed. " +
            "The app will close safely; reopen it and try again.\n\n" + e.Exception.Message,
            "LocalPhotoPDF",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
