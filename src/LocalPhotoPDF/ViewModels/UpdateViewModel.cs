using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LocalPhotoPDF.Core;
using LocalPhotoPDF.Infrastructure;

namespace LocalPhotoPDF.ViewModels;

/// <summary>
/// Drives the one control in this application that uses the network.
/// </summary>
/// <remarks>
/// <para>
/// This lives apart from <see cref="MainViewModel"/> on purpose. That class is already 800 lines
/// and does the photo work; bolting an update flow onto it would make the largest untested file in
/// the project larger still. Everything here is a thin shell over <see cref="IUpdateService"/>,
/// which is in the tested library.
/// </para>
/// <para>
/// 🔒 There is no timer and no startup call. The only thing that starts a network request is
/// <see cref="UpdateCommand"/>, and the only thing that runs the downloaded installer is a second,
/// separate press after the checksum has been verified. Nobody is updated without deciding to be.
/// </para>
/// </remarks>
internal sealed class UpdateViewModel : ObservableObject
{
    private readonly Func<IUpdateService> _createService;
    private readonly Action<Exception> _reportUnexpectedFailure;
    private readonly Func<string, bool> _confirmInstall;

    private AvailableUpdate? _ready;
    private VerifiedInstaller? _verified;
    private bool _busy;
    private string _status = string.Empty;
    private string _buttonText = "Check for _updates";

    public UpdateViewModel(
        Func<IUpdateService>? createService = null,
        Action<Exception>? reportUnexpectedFailure = null,
        Func<string, bool>? confirmInstall = null)
    {
        _createService = createService ?? (() => new UpdateService(UpdateService.DefaultRepositorySlug));
        _reportUnexpectedFailure = reportUnexpectedFailure ?? (_ => { });
        _confirmInstall = confirmInstall ?? (_ => true);
        UpdateCommand = new RelayCommand(async () => await RunAsync().ConfigureAwait(true), () => !_busy);
    }

    public RelayCommand UpdateCommand { get; }

    /// <summary>
    /// The running version, taken from the assembly rather than written down twice.
    /// </summary>
    /// <remarks>
    /// Read once into a field instead of computed per binding: reflection on every property read
    /// would be wasteful, and a data binding cannot target a static member.
    /// </remarks>
    public string VersionLabel { get; } = "Version " + CurrentVersion.ToString();

    public string UpdateStatus
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasUpdateStatus));
            }
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_status);

    public string UpdateButtonText
    {
        get => _buttonText;
        private set => SetProperty(ref _buttonText, value);
    }

    /// <summary>
    /// The version of the running assembly, as a three-part release version.
    /// </summary>
    /// <remarks>
    /// The build stamps this from the release tag. A debug build has no such stamp and reads 1.0.0,
    /// which is harmless: the check simply reports that a published release is newer.
    /// </remarks>
    public static ReleaseVersion CurrentVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            // The SDK appends "+<commit sha>" to the informational version.
            var plus = informational?.IndexOf('+', StringComparison.Ordinal) ?? -1;
            if (plus >= 0)
            {
                informational = informational![..plus];
            }

            if (ReleaseVersion.TryParse(informational, out var parsed))
            {
                return parsed;
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            return assemblyVersion is null
                ? new ReleaseVersion(1, 0, 0)
                : new ReleaseVersion(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }
    }

    private async Task RunAsync()
    {
        if (_busy)
        {
            return;
        }

        // A second press once an update is verified and waiting means "install it now".
        if (_ready is not null && _verified is not null)
        {
            await LaunchInstallerAsync(_verified).ConfigureAwait(true);
            return;
        }

        SetBusy(true);
        var service = _createService();
        try
        {
            await CheckAndPrepareAsync(service).ConfigureAwait(true);
        }
        catch (UpdateVerificationException failure)
        {
            // Never silently retried and never treated as "no update". The bytes were not the
            // bytes that were published, and the person deserves to be told exactly that.
            UpdateStatus = failure.Message;
            UpdateButtonText = "Check for _updates";
            _ready = null;
            _verified = null;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException)
        {
            UpdateStatus = "Could not reach GitHub. Nothing was changed.";
            UpdateButtonText = "Check for _updates";
        }
        catch (Exception failure)
        {
            _reportUnexpectedFailure(failure);
            UpdateStatus = "The update check did not complete. Nothing was changed.";
            UpdateButtonText = "Check for _updates";
        }
        finally
        {
            (service as IDisposable)?.Dispose();
            SetBusy(false);
        }
    }

    private async Task CheckAndPrepareAsync(IUpdateService service)
    {
        UpdateStatus = "Checking…";
        var result = await service.CheckAsync(CurrentVersion, CancellationToken.None).ConfigureAwait(true);

        switch (result.Outcome)
        {
            case UpdateCheckOutcome.AlreadyUpToDate:
                UpdateStatus = "You have the latest version.";
                return;

            case UpdateCheckOutcome.NoReleasesPublished:
                UpdateStatus = "No release has been published yet.";
                return;

            case UpdateCheckOutcome.ReleaseIsNotUsable:
                UpdateStatus = "The latest release is missing the files an update needs.";
                return;

            case UpdateCheckOutcome.UpdateAvailable:
                break;

            default:
                return;
        }

        var update = result.Update!;
        if (!_confirmInstall(
                $"Version {update.Version} is available. Download {UpdateService.DescribeSize(update.InstallerSizeBytes)} and install it?"))
        {
            UpdateStatus = $"Version {update.Version} is available.";
            return;
        }

        UpdateStatus = "Downloading…";
        var progress = new Progress<double>(fraction =>
            UpdateStatus = "Downloading… " + (fraction * 100).ToString("0", CultureInfo.CurrentCulture) + "%");

        _verified = await service
            .DownloadVerifiedInstallerAsync(update, progress, CancellationToken.None)
            .ConfigureAwait(true);
        _ready = update;

        UpdateStatus = $"Version {update.Version} is ready and verified.";
        UpdateButtonText = "_Install and restart";
    }

    /// <summary>
    /// Re-verifies the installer, starts it, and closes this application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The re-check is the point. The download was proven genuine when it landed, but it then
    /// waited in a temporary directory that any process running as this user can write to, under a
    /// fixed and publicly known name prefix, for as long as the person took to press Install. A
    /// single verification at download time describes the file that arrived, not the file about to
    /// be executed. Hashing it again here costs a local read of about 45 MB and no network at all.
    /// </para>
    /// <para>
    /// Closing matters too: the installer refuses to run while LocalPhotoPDF holds its
    /// single-instance mutex, and a running executable cannot be overwritten. Shutting down here is
    /// what makes the upgrade succeed rather than show "close LocalPhotoPDF and try again".
    /// </para>
    /// </remarks>
    private async Task LaunchInstallerAsync(VerifiedInstaller installer)
    {
        UpdateStatus = "Checking the download again…";

        // Held open across Process.Start below, so the file cannot be swapped, renamed or deleted
        // between passing this check and Windows loading it. Closing it first would only narrow
        // the window that the re-check exists to remove.
        FileStream? verified;
        try
        {
            verified = await UpdateService
                .OpenIfMatchesAsync(installer.Path, installer.Sha256, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            verified = null;
        }

        if (verified is null)
        {
            // Deliberately does not retry or silently re-download. The file that was proven
            // genuine is not the file on disk any more, and that is worth telling someone.
            _ready = null;
            _verified = null;
            UpdateButtonText = "Check for _updates";
            UpdateStatus = "The downloaded update changed after it was checked, so it was not run. Check for updates again.";
            return;
        }

        var installerPath = installer.Path;
        try
        {
            await using (verified)
            {
                // /FROMAPP tells the installer this application is closing right now, so it waits
                // for the single-instance mutex instead of giving up the instant it finds it held.
                // Without it the update was a race against WPF shutdown, and losing meant the app
                // closed and the installer refused to run.
                Process.Start(new ProcessStartInfo(installerPath)
                {
                    Arguments = "/FROMAPP",
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception failure)
        {
            _reportUnexpectedFailure(failure);
            UpdateStatus = "The installer could not be started. It is in your temporary files.";
            return;
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateCommand.RaiseCanExecuteChanged();
    }
}
