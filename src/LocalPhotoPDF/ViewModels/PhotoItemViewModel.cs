using System.Globalization;
using System.Windows.Media.Imaging;
using LocalPhotoPDF.Core;
using LocalPhotoPDF.Infrastructure;

namespace LocalPhotoPDF.ViewModels;

internal sealed class PhotoItemViewModel : ObservableObject
{
    private int _position;
    private int _rotationDegrees;

    public PhotoItemViewModel(ImageInfo image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }

    public ImageInfo Image { get; }

    public string FilePath => Image.FilePath;

    public string DisplayName => Image.DisplayName;

    public string Format => Image.Format;

    public BitmapSource Thumbnail => Image.Thumbnail;

    public int Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public int RotationDegrees
    {
        get => _rotationDegrees;
        private set
        {
            if (SetProperty(ref _rotationDegrees, value))
            {
                OnPropertyChanged(nameof(DisplayDimensions));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public string DisplayDimensions
    {
        get
        {
            var swapsAxes = RotationDegrees is 90 or 270;
            var width = swapsAxes ? Image.PixelHeight : Image.PixelWidth;
            var height = swapsAxes ? Image.PixelWidth : Image.PixelHeight;
            return string.Create(CultureInfo.InvariantCulture, $"{width:N0} × {height:N0} px");
        }
    }

    public string DisplayFileSize => FormatFileSize(Image.FileSizeBytes);

    public string Details => $"{DisplayDimensions}  •  {Format}  •  {DisplayFileSize}";

    public string AccessibleName => $"Photo {Position}: {DisplayName}, {DisplayDimensions}, {DisplayFileSize}";

    public PhotoSource ToPhotoSource()
    {
        return new PhotoSource(FilePath, RotationDegrees);
    }

    public void RotateLeft()
    {
        RotationDegrees = NormalizeRotation(RotationDegrees - 90);
        OnPropertyChanged(nameof(Details));
    }

    public void RotateRight()
    {
        RotationDegrees = NormalizeRotation(RotationDegrees + 90);
        OnPropertyChanged(nameof(Details));
    }

    private static int NormalizeRotation(int value)
    {
        return ((value % 360) + 360) % 360;
    }

    private static string FormatFileSize(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)byteCount;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "N0" : "N1";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {units[unitIndex]}";
    }
}
