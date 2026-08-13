using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LocalPhotoPDF.Services;
using LocalPhotoPDF.ViewModels;
using Microsoft.Win32;

namespace LocalPhotoPDF;

public partial class MainWindow : Window
{
    private const string PhotoDragFormat = "LocalPhotoPDF.PhotoItem";

    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private Point _dragStartPoint;
    private PhotoItemViewModel? _dragCandidate;
    private bool _closeAfterCancellation;
    private bool _forceClose;

    internal MainWindow(
        MainViewModel viewModel,
        SettingsService settingsService,
        AppSettings settings)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        InitializeComponent();
        DataContext = _viewModel;
        RestoreWindowPlacement();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void AddPhotosButton_Click(object sender, RoutedEventArgs e)
    {
        await ChooseAndImportPhotosAsync();
    }

    private async void DropZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CanEdit)
        {
            await ChooseAndImportPhotosAsync();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_viewModel.CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!_viewModel.CanEdit || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _viewModel.ImportFilesAsync(paths);
        }
    }

    private void PhotoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(PhotoList);
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragCandidate = null;
            return;
        }

        _dragCandidate = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext
            as PhotoItemViewModel;
    }

    private void PhotoList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null || !_viewModel.CanEdit)
        {
            return;
        }

        var currentPoint = e.GetPosition(PhotoList);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(PhotoDragFormat, _dragCandidate);
        DragDrop.DoDragDrop(PhotoList, data, DragDropEffects.Move);
        _dragCandidate = null;
    }

    private void PhotoList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PhotoDragFormat))
        {
            e.Effects = _viewModel.CanEdit ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void PhotoList_Drop(object sender, DragEventArgs e)
    {
        if (!_viewModel.CanEdit
            || e.Data.GetData(PhotoDragFormat) is not PhotoItemViewModel draggedPhoto)
        {
            return;
        }

        var targetPhoto = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext
            as PhotoItemViewModel;
        if (targetPhoto is null && _viewModel.Photos.Count != 0)
        {
            targetPhoto = _viewModel.Photos[^1];
        }

        if (targetPhoto is not null)
        {
            _viewModel.ReorderPhoto(draggedPhoto, targetPhoto);
            PhotoList.ScrollIntoView(draggedPhoto);
        }

        e.Handled = true;
    }

    private async void GeneratePdfButton_Click(object sender, RoutedEventArgs e)
    {
        await ChooseAndGeneratePdfAsync();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            e.Handled = true;
            await ChooseAndImportPhotosAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.G)
        {
            e.Handled = true;
            await ChooseAndGeneratePdfAsync();
            return;
        }

        if (e.Key == Key.Escape && _viewModel.CancelCommand.CanExecute(null))
        {
            _viewModel.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _viewModel.SelectedPhoto is { } selected
                                && _viewModel.RemoveCommand.CanExecute(selected))
        {
            _viewModel.RemoveCommand.Execute(selected);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && _viewModel.SelectedPhoto is { } photo)
        {
            var command = e.Key switch
            {
                Key.Up => _viewModel.MoveUpCommand,
                Key.Down => _viewModel.MoveDownCommand,
                _ => null,
            };
            if (command?.CanExecute(photo) == true)
            {
                command.Execute(photo);
                PhotoList.ScrollIntoView(photo);
                e.Handled = true;
            }
        }
    }

    private async Task ChooseAndImportPhotosAsync()
    {
        if (!_viewModel.CanEdit)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Add photos to LocalPhotoPDF",
            Filter = _viewModel.SupportedPhotoFilter,
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true,
            RestoreDirectory = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ImportFilesAsync(dialog.FileNames);
            PhotoList.ScrollIntoView(_viewModel.Photos.LastOrDefault());
        }
    }

    private async Task ChooseAndGeneratePdfAsync()
    {
        if (!_viewModel.CanGenerate)
        {
            return;
        }

        var firstName = Path.GetFileNameWithoutExtension(_viewModel.Photos[0].DisplayName);
        var safeName = string.Concat(firstName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        var dialog = new SaveFileDialog
        {
            Title = "Save your PDF",
            Filter = "PDF document|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            RestoreDirectory = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = string.IsNullOrWhiteSpace(safeName) ? "photos.pdf" : $"{safeName}-photos.pdf",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.GeneratePdfAsync(dialog.FileName);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // IsBusy, not IsGenerating: importing also runs a long decode loop, and closing during it
        // used to fall straight through and dispose the view model while that loop was running.
        if (_viewModel.IsBusy && !_forceClose)
        {
            var result = MessageBox.Show(
                this,
                "LocalPhotoPDF is still working on your photos. Stop safely and close?",
                "LocalPhotoPDF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            // MessageBox.Show pumps the message queue, so the import or the generation can finish
            // while the prompt is on screen. If it did, IsBusy already went false and
            // ViewModel_PropertyChanged has already fired — it will never fire again, so the
            // cancel-then-close-later path below would disable the window forever and leave
            // Task Manager as the only way out. Nothing left to cancel, so just close normally.
            if (_viewModel.IsBusy)
            {
                e.Cancel = true;
                _closeAfterCancellation = true;
                IsEnabled = false;
                _viewModel.CancelCommand.Execute(null);
                return;
            }
        }

        SaveWindowPlacement();
        _viewModel.SavePreferences();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy)
            && !_viewModel.IsBusy
            && _closeAfterCancellation)
        {
            _forceClose = true;
            Dispatcher.BeginInvoke(Close);
        }
    }

    private void RestoreWindowPlacement()
    {
        Width = IsReasonableDimension(_settings.WindowWidth, MinWidth) ? _settings.WindowWidth : 1180;
        Height = IsReasonableDimension(_settings.WindowHeight, MinHeight) ? _settings.WindowHeight : 780;

        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
        {
            var candidate = new Rect(left, top, Width, Height);
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            var visibleArea = Rect.Intersect(candidate, virtualScreen);
            if (!visibleArea.IsEmpty && visibleArea.Width >= 64 && visibleArea.Height >= 64)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (_settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowPlacement()
    {
        _settings.IsMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settingsService.Save(_settings);
    }

    private static bool IsReasonableDimension(double value, double minimum)
    {
        return double.IsFinite(value) && value >= minimum && value <= 10_000;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
