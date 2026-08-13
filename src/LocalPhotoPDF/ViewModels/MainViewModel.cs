using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using LocalPhotoPDF.Core;
using LocalPhotoPDF.Infrastructure;
using LocalPhotoPDF.Services;

namespace LocalPhotoPDF.ViewModels;

internal sealed class MainViewModel : ObservableObject, IDisposable
{

    private readonly IImageImportService _imageImportService;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ShellService _shellService;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly HashSet<string> _photoPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly RelayCommand<PhotoItemViewModel> _moveUpCommand;
    private readonly RelayCommand<PhotoItemViewModel> _moveDownCommand;
    private readonly RelayCommand<PhotoItemViewModel> _rotateLeftCommand;
    private readonly RelayCommand<PhotoItemViewModel> _rotateRightCommand;
    private readonly RelayCommand<PhotoItemViewModel> _removeCommand;
    private readonly RelayCommand _clearCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _openPdfCommand;
    private readonly RelayCommand _showInFolderCommand;
    private readonly RelayCommand _dismissNoticeCommand;

    private PhotoItemViewModel? _selectedPhoto;
    private PdfPageSize _pageSize;
    private PdfMargin _margin;
    private PdfQuality _quality;
    private bool _isImporting;
    private bool _isGenerating;
    private double _progressPercentage;
    private string _progressText = string.Empty;
    private string? _lastOutputPath;
    private string _noticeTitle = string.Empty;
    private string _noticeMessage = string.Empty;
    private string _noticeIcon = "ℹ";
    private bool _hasNotice;
    private CancellationTokenSource? _generationCancellation;
    private CancellationTokenSource? _importCancellation;
    private bool _isDisposed;

    public MainViewModel(
        IImageImportService imageImportService,
        IPdfGenerationService pdfGenerationService,
        ShellService shellService,
        SettingsService settingsService,
        AppSettings settings)
    {
        _imageImportService = imageImportService ?? throw new ArgumentNullException(nameof(imageImportService));
        _pdfGenerationService = pdfGenerationService ?? throw new ArgumentNullException(nameof(pdfGenerationService));
        _shellService = shellService ?? throw new ArgumentNullException(nameof(shellService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _pageSize = Enum.IsDefined(settings.PageSize) ? settings.PageSize : PdfPageSize.A4;
        _margin = Enum.IsDefined(settings.Margin) ? settings.Margin : PdfMargin.FiveMillimeters;
        _quality = Enum.IsDefined(settings.Quality) ? settings.Quality : PdfQuality.Balanced;

        PageSizeChoices =
        [
            new(PdfPageSize.A4, "A4 — automatic orientation", "Fits each photo to an A4 portrait or landscape page."),
            new(PdfPageSize.MatchPhoto, "Match each photo", "Makes every PDF page match its photo's proportions."),
            new(PdfPageSize.Letter, "US Letter — automatic orientation", "Fits each photo to a US Letter portrait or landscape page."),
        ];
        MarginChoices =
        [
            new(PdfMargin.None, "None", "Uses the full printable page."),
            new(PdfMargin.FiveMillimeters, "5 mm", "Adds a small clean border around each photo."),
            new(PdfMargin.TenMillimeters, "10 mm", "Adds a wider border around each photo."),
        ];
        QualityChoices =
        [
            new(PdfQuality.High, "High", "Keeps more detail and creates a larger PDF."),
            new(PdfQuality.Balanced, "Balanced", "Clear photos with a sensible file size."),
            new(PdfQuality.Small, "Small", "Reduces detail to make sharing easier."),
        ];

        _moveUpCommand = new RelayCommand<PhotoItemViewModel>(item => MovePhoto(item, -1), CanMoveUp);
        _moveDownCommand = new RelayCommand<PhotoItemViewModel>(item => MovePhoto(item, 1), CanMoveDown);
        _rotateLeftCommand = new RelayCommand<PhotoItemViewModel>(item => RotatePhoto(item, false), CanEditPhoto);
        _rotateRightCommand = new RelayCommand<PhotoItemViewModel>(item => RotatePhoto(item, true), CanEditPhoto);
        _removeCommand = new RelayCommand<PhotoItemViewModel>(RemovePhoto, CanEditPhoto);
        _clearCommand = new RelayCommand(ClearPhotos, () => HasPhotos && !IsBusy);
        _cancelCommand = new RelayCommand(CancelCurrentWork, () => IsGenerating || IsImporting);
        _openPdfCommand = new RelayCommand(OpenPdf, HasUsableOutput);
        _showInFolderCommand = new RelayCommand(ShowInFolder, HasUsableOutput);
        _dismissNoticeCommand = new RelayCommand(() => HasNotice = false, () => HasNotice);
    }

    public ObservableCollection<PhotoItemViewModel> Photos { get; } = [];

    public IReadOnlyList<OptionChoice<PdfPageSize>> PageSizeChoices { get; }

    public IReadOnlyList<OptionChoice<PdfMargin>> MarginChoices { get; }

    public IReadOnlyList<OptionChoice<PdfQuality>> QualityChoices { get; }

    public ICommand MoveUpCommand => _moveUpCommand;

    public ICommand MoveDownCommand => _moveDownCommand;

    public ICommand RotateLeftCommand => _rotateLeftCommand;

    public ICommand RotateRightCommand => _rotateRightCommand;

    public ICommand RemoveCommand => _removeCommand;

    public ICommand ClearCommand => _clearCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand OpenPdfCommand => _openPdfCommand;

    public ICommand ShowInFolderCommand => _showInFolderCommand;

    public ICommand DismissNoticeCommand => _dismissNoticeCommand;

    public PhotoItemViewModel? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (SetProperty(ref _selectedPhoto, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public PdfPageSize PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                _settings.PageSize = value;
                OnPropertyChanged(nameof(PageSizeDescription));
                InvalidateOutput();
            }
        }
    }

    public PdfMargin Margin
    {
        get => _margin;
        set
        {
            if (SetProperty(ref _margin, value))
            {
                _settings.Margin = value;
                OnPropertyChanged(nameof(MarginDescription));
                InvalidateOutput();
            }
        }
    }

    public PdfQuality Quality
    {
        get => _quality;
        set
        {
            if (SetProperty(ref _quality, value))
            {
                _settings.Quality = value;
                OnPropertyChanged(nameof(QualityDescription));
                InvalidateOutput();
            }
        }
    }

    public string PageSizeDescription => FindDescription(PageSizeChoices, PageSize);

    public string MarginDescription => FindDescription(MarginChoices, Margin);

    public string QualityDescription => FindDescription(QualityChoices, Quality);

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsBusy => IsImporting || IsGenerating;

    public bool CanEdit => !IsBusy;

    public bool CanGenerate => HasPhotos && !IsBusy;

    public bool HasPhotos => Photos.Count != 0;

    public bool IsEmpty => !HasPhotos;

    public string PhotoCountText => Photos.Count == 1 ? "1 photo" : $"{Photos.Count} photos";

    public string ImportStatus => IsImporting ? "Checking and adding photos…" : string.Empty;

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetProperty(ref _progressPercentage, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string? LastOutputPath
    {
        get => _lastOutputPath;
        private set
        {
            if (SetProperty(ref _lastOutputPath, value))
            {
                OnPropertyChanged(nameof(HasOutput));
                OnPropertyChanged(nameof(OutputFileName));
                RaiseCommandStates();
            }
        }
    }

    public bool HasOutput => !string.IsNullOrWhiteSpace(LastOutputPath) && File.Exists(LastOutputPath);

    public string OutputFileName => string.IsNullOrWhiteSpace(LastOutputPath)
        ? string.Empty
        : Path.GetFileName(LastOutputPath);

    public bool HasNotice
    {
        get => _hasNotice;
        private set
        {
            if (SetProperty(ref _hasNotice, value))
            {
                _dismissNoticeCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string NoticeTitle
    {
        get => _noticeTitle;
        private set => SetProperty(ref _noticeTitle, value);
    }

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public string NoticeIcon
    {
        get => _noticeIcon;
        private set => SetProperty(ref _noticeIcon, value);
    }

    public string SupportedPhotoFilter
    {
        get
        {
            var patterns = _imageImportService.SupportedExtensions
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .Select(extension => $"*{extension}");
            return $"Supported photos|{string.Join(';', patterns)}|All files|*.*";
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (IsBusy)
        {
            return;
        }

        var importFailures = new List<ImportFailure>();
        var candidates = new List<string>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var suppliedPath in filePaths)
        {
            if (Photos.Count + candidates.Count >= ImportLimits.MaximumPhotoCount)
            {
                importFailures.Add(new ImportFailure(suppliedPath, $"The app can hold up to {ImportLimits.MaximumPhotoCount} photos."));
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(suppliedPath);
            }
            catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
            {
                importFailures.Add(new ImportFailure(suppliedPath, "This file path is not valid."));
                continue;
            }

            if (_photoPaths.Contains(normalizedPath) || !seenCandidates.Add(normalizedPath))
            {
                importFailures.Add(new ImportFailure(suppliedPath, "This photo is already in the list."));
                continue;
            }

            candidates.Add(normalizedPath);
        }

        if (candidates.Count == 0)
        {
            if (importFailures.Count != 0)
            {
                ShowImportSummary(0, importFailures);
            }

            return;
        }

        IsImporting = true;
        HasNotice = false;
        LastOutputPath = null;
        try
        {
            _importCancellation = new CancellationTokenSource();
            var result = await _imageImportService
                .ImportAsync(candidates, _importCancellation.Token)
                .ConfigureAwait(true);
            foreach (var image in result.Images)
            {
                if (_photoPaths.Add(Path.GetFullPath(image.FilePath)))
                {
                    Photos.Add(new PhotoItemViewModel(image));
                }
            }

            importFailures.AddRange(result.Failures);
            ReindexPhotos();
            if (SelectedPhoto is null && Photos.Count != 0)
            {
                SelectedPhoto = Photos[0];
            }

            ShowImportSummary(result.Images.Count, importFailures);
        }
        catch (OperationCanceledException)
        {
            ShowNotice("Import cancelled", "No additional photos were added.", "ℹ");
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ShowNotice(
                "Photos could not be added",
                GetFriendlyError(exception),
                "!");
        }
        finally
        {
            _importCancellation?.Dispose();
            _importCancellation = null;
            IsImporting = false;
            NotifyPhotoCollectionChanged();
        }
    }

    public async Task GeneratePdfAsync(string outputPath)
    {
        if (!CanGenerate)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        IsGenerating = true;
        HasNotice = false;
        LastOutputPath = null;
        ProgressPercentage = 0;
        ProgressText = $"Preparing {Photos.Count} photos…";
        _generationCancellation = new CancellationTokenSource();

        var photos = Photos.Select(photo => photo.ToPhotoSource()).ToArray();
        var request = new PdfBuildRequest(
            photos,
            outputPath,
            new PdfBuildOptions(PageSize, Margin, Quality));
        var progress = new Progress<PdfBuildProgress>(UpdateProgress);

        try
        {
            await _pdfGenerationService
                .GenerateAsync(request, progress, _generationCancellation.Token)
                .ConfigureAwait(true);

            ProgressPercentage = 100;
            ProgressText = $"Finished {photos.Length} of {photos.Length} photos";
            LastOutputPath = Path.GetFullPath(outputPath);
            ShowNotice(
                "Your PDF is ready",
                $"Created {OutputFileName} with {photos.Length} {(photos.Length == 1 ? "page" : "pages")}.",
                "✓");
        }
        catch (OperationCanceledException)
        {
            ShowNotice(
                "PDF creation cancelled",
                "No existing PDF was changed.",
                "ℹ");
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ShowNotice(
                "The PDF could not be created",
                GetFriendlyError(exception),
                "!");
        }
        finally
        {
            _generationCancellation.Dispose();
            _generationCancellation = null;
            IsGenerating = false;
        }
    }

    public void ReorderPhoto(PhotoItemViewModel draggedPhoto, PhotoItemViewModel targetPhoto)
    {
        ArgumentNullException.ThrowIfNull(draggedPhoto);
        ArgumentNullException.ThrowIfNull(targetPhoto);
        if (IsBusy || ReferenceEquals(draggedPhoto, targetPhoto))
        {
            return;
        }

        var oldIndex = Photos.IndexOf(draggedPhoto);
        var targetIndex = Photos.IndexOf(targetPhoto);
        if (oldIndex < 0 || targetIndex < 0)
        {
            return;
        }

        Photos.Move(oldIndex, targetIndex);
        SelectedPhoto = draggedPhoto;
        InvalidateOutput();
        ReindexPhotos();
        RaiseCommandStates();
    }

    public void SavePreferences()
    {
        _settings.PageSize = PageSize;
        _settings.Margin = Margin;
        _settings.Quality = Quality;
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _generationCancellation?.Cancel();
        _generationCancellation?.Dispose();
        _generationCancellation = null;
        _importCancellation?.Cancel();
        _importCancellation?.Dispose();
        _importCancellation = null;
        _isDisposed = true;
    }

    private void MovePhoto(PhotoItemViewModel? photo, int offset)
    {
        if (photo is null || IsBusy)
        {
            return;
        }

        var oldIndex = Photos.IndexOf(photo);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Photos.Count)
        {
            return;
        }

        Photos.Move(oldIndex, newIndex);
        SelectedPhoto = photo;
        InvalidateOutput();
        ReindexPhotos();
        RaiseCommandStates();
    }

    private void RemovePhoto(PhotoItemViewModel? photo)
    {
        if (photo is null || IsBusy)
        {
            return;
        }

        var oldIndex = Photos.IndexOf(photo);
        if (oldIndex < 0)
        {
            return;
        }

        Photos.RemoveAt(oldIndex);
        _photoPaths.Remove(photo.FilePath);
        SelectedPhoto = Photos.Count == 0 ? null : Photos[Math.Min(oldIndex, Photos.Count - 1)];
        LastOutputPath = null;
        ReindexPhotos();
        NotifyPhotoCollectionChanged();
    }

    private void RotatePhoto(PhotoItemViewModel? photo, bool clockwise)
    {
        if (!CanEditPhoto(photo))
        {
            return;
        }

        if (clockwise)
        {
            photo!.RotateRight();
        }
        else
        {
            photo!.RotateLeft();
        }

        InvalidateOutput();
    }

    private void ClearPhotos()
    {
        Photos.Clear();
        _photoPaths.Clear();
        SelectedPhoto = null;
        LastOutputPath = null;
        HasNotice = false;
        NotifyPhotoCollectionChanged();
    }

    private void InvalidateOutput()
    {
        if (LastOutputPath is null)
        {
            return;
        }

        LastOutputPath = null;
        if (NoticeTitle.Equals("Your PDF is ready", StringComparison.Ordinal))
        {
            HasNotice = false;
        }
    }

    private bool CanMoveUp(PhotoItemViewModel? photo)
    {
        return photo is not null && !IsBusy && Photos.IndexOf(photo) > 0;
    }

    private bool CanMoveDown(PhotoItemViewModel? photo)
    {
        var index = photo is null ? -1 : Photos.IndexOf(photo);
        return !IsBusy && index >= 0 && index < Photos.Count - 1;
    }

    private bool CanEditPhoto(PhotoItemViewModel? photo)
    {
        return photo is not null && !IsBusy && Photos.Contains(photo);
    }

    private void CancelCurrentWork()
    {
        // Cancel covers import as well as generation. Importing sets IsBusy, which disables the
        // whole UI, so without this a large import left the window inert for minutes with no way
        // out — and closing during it disposed the view model while the decode loop was still
        // running.
        if (_generationCancellation is { IsCancellationRequested: false })
        {
            ProgressText = "Cancelling safely…";
            _generationCancellation.Cancel();
        }

        if (_importCancellation is { IsCancellationRequested: false })
        {
            _importCancellation.Cancel();
        }
    }

    private bool HasUsableOutput()
    {
        return HasOutput;
    }

    private void OpenPdf()
    {
        TryUseOutput(_shellService.OpenFile);
    }

    private void ShowInFolder()
    {
        TryUseOutput(_shellService.ShowInFolder);
    }

    private void TryUseOutput(Action<string> action)
    {
        if (string.IsNullOrWhiteSpace(LastOutputPath))
        {
            return;
        }

        try
        {
            action(LastOutputPath);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ShowNotice("The PDF could not be opened", GetFriendlyError(exception), "!");
            LastOutputPath = null;
        }
    }

    private void UpdateProgress(PdfBuildProgress progress)
    {
        ProgressPercentage = progress.Percentage;
        var currentName = string.IsNullOrWhiteSpace(progress.CurrentFilePath)
            ? string.Empty
            : $" — {Path.GetFileName(progress.CurrentFilePath)}";
        ProgressText = $"Processed {progress.CompletedPhotos} of {progress.TotalPhotos}{currentName}";
    }

    private void ShowImportSummary(int importedCount, List<ImportFailure> failures)
    {
        if (failures.Count == 0)
        {
            var noun = importedCount == 1 ? "photo" : "photos";
            ShowNotice($"Added {importedCount} {noun}", "Drag the rows or use the arrow buttons to set the page order.", "✓");
            return;
        }

        var title = importedCount == 0
            ? "No photos were added"
            : $"Added {importedCount} {(importedCount == 1 ? "photo" : "photos")} with {failures.Count} skipped";
        var details = failures
            .Take(4)
            .Select(failure => $"{SafeFileName(failure.FilePath)}: {failure.Message}");
        var message = string.Join(Environment.NewLine, details);
        if (failures.Count > 4)
        {
            message += $"{Environment.NewLine}…and {failures.Count - 4} more.";
        }

        if (failures.Any(FailureMayNeedCodec))
        {
            message += Environment.NewLine + Environment.NewLine +
                       "For HEIC, WebP, AVIF, or camera RAW files, install the matching image extension from the Microsoft Store, then try again.";
        }

        ShowNotice(title, message, "!");
    }

    private static bool FailureMayNeedCodec(ImportFailure failure)
    {
        var message = failure.Message;
        var extension = Path.GetExtension(failure.FilePath);
        return message.Contains("codec", StringComparison.OrdinalIgnoreCase)
               || message.Contains("could not decode", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".heic", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".avif", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".raw", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dng", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowNotice(string title, string message, string icon)
    {
        NoticeTitle = title;
        NoticeMessage = message;
        NoticeIcon = icon;
        HasNotice = true;
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(ImportStatus));
        RaiseCommandStates();
    }

    private void NotifyPhotoCollectionChanged()
    {
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PhotoCountText));
        OnPropertyChanged(nameof(CanGenerate));
        RaiseCommandStates();
    }

    private void ReindexPhotos()
    {
        for (var index = 0; index < Photos.Count; index++)
        {
            Photos[index].Position = index + 1;
        }
    }

    private void RaiseCommandStates()
    {
        _moveUpCommand.RaiseCanExecuteChanged();
        _moveDownCommand.RaiseCanExecuteChanged();
        _rotateLeftCommand.RaiseCanExecuteChanged();
        _rotateRightCommand.RaiseCanExecuteChanged();
        _removeCommand.RaiseCanExecuteChanged();
        _clearCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _openPdfCommand.RaiseCanExecuteChanged();
        _showInFolderCommand.RaiseCanExecuteChanged();
    }

    private static string FindDescription<T>(IEnumerable<OptionChoice<T>> choices, T selected)
        where T : struct, Enum
    {
        return choices.First(choice => EqualityComparer<T>.Default.Equals(choice.Value, selected)).Description;
    }

    private static string SafeFileName(string filePath)
    {
        try
        {
            return Path.GetFileName(filePath);
        }
        catch (ArgumentException)
        {
            return "Photo";
        }
    }

    private static bool IsRecoverable(Exception exception)
    {
        // COMException and OutOfMemoryException are both raised by Windows Imaging Component
        // while decoding or encoding a photo, and WicImageImportService.IsRecoverableImportFailure
        // already treats them as recoverable. This list did not, so the same failure was handled
        // politely during import but escaped during generation — out through the async void click
        // handler to App.OnDispatcherUnhandledException, which calls Current.Shutdown(1). One bad
        // photo part-way through a large batch therefore closed the app and lost the whole list.
        return exception is ArgumentException
            or FileNotFoundException
            or DirectoryNotFoundException
            or UnauthorizedAccessException
            or IOException
            or InvalidDataException
            or NotSupportedException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or OutOfMemoryException;
    }

    private static string GetFriendlyError(Exception exception)
    {
        return exception switch
        {
            PdfTemporaryFileCleanupException => exception.Message,
            UnauthorizedAccessException => "Windows denied access to that location. Choose a folder you can write to and try again.",
            DirectoryNotFoundException => "That folder no longer exists. Choose another location and try again.",
            FileNotFoundException => "One of the selected files can no longer be found.",
            IOException => "Windows could not read or write the file. Check that it is not open in another app and that there is enough free disk space.",
            NotSupportedException => "This photo format is not available through Windows on this PC. A matching Windows image codec may need to be installed.",
            _ => exception.Message,
        };
    }
}
