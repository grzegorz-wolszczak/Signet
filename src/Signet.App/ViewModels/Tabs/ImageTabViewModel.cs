using System.Globalization;
using System.IO;
using System;
using Avalonia.Media.Imaging;
using Signet.App.Imaging;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Raster image preview tab: the preview, an info line (dimensions / size / DPI), zoom, "Image Resize"
/// (scaling with saving). There is no rotation, cropping or history.
/// </summary>
public sealed class ImageTabViewModel : ContentTabViewModel
{
    private readonly ImageResource _image;
    private readonly IStatusBarService _statusBar;
    private readonly Book? _book;

    private Bitmap? _bitmap;
    private (int Width, int Height) _dimensions;
    private long _fileSize;
    private (int X, int Y) _dpi;

    /// <summary>Creates the image preview tab.</summary>
    public ImageTabViewModel(OpenTab tab, IStatusBarService statusBar, Book? book)
        : base(tab)
    {
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _book = book;

        if (tab.Resource is not ImageResource image)
        {
            throw new ArgumentException(
                $"ImageTab requires an image resource, got {tab.Resource.GetType().Name}.", nameof(tab));
        }

        _image = image;
        _image.ResourceUpdatedOnDisk += OnResourceUpdatedOnDisk;
        LoadMetadata();
    }

    /// <summary>Request: show the "Resize Image" window (the view then calls <see cref="ApplyResize"/>).</summary>
    public event EventHandler? ResizeRequested;

    /// <summary>The bitmap to display, or <c>null</c> when the file could not be loaded.</summary>
    public Bitmap? Bitmap => _bitmap;

    /// <summary>Image width in pixels (0 when unknown).</summary>
    public int PixelWidth => _dimensions.Width;

    /// <summary>Image height in pixels (0 when unknown).</summary>
    public int PixelHeight => _dimensions.Height;

    /// <summary>Info line: <c>"W×H px | N KB | type | DPI"</c>.</summary>
    public string InfoLine
    {
        get
        {
            string dims = _dimensions is { Width: > 0, Height: > 0 }
                ? $"{_dimensions.Width}×{_dimensions.Height} px"
                : Strings.Get("ImageTab_UnknownDimensions");
            string size = string.Create(CultureInfo.InvariantCulture, $"{(_fileSize + 512) / 1024} KB");
            string dpi = _dpi is { X: > 0 }
                ? $" | {_dpi.X}×{_dpi.Y} DPI"
                : string.Empty;
            return $"{dims} | {size} | {_image.MediaType}{dpi}";
        }
    }

    /// <summary>Raises a request to show the resize window.</summary>
    public void RequestResize() => ResizeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Scales the image file to the given dimensions and overwrites it.
    /// Changing the dimensions marks the publication as modified.
    /// </summary>
    public void ApplyResize(int width, int height)
    {
        if (width < 1 || height < 1 || (width == _dimensions.Width && height == _dimensions.Height))
        {
            return;
        }

        bool ok;
        try
        {
            ok = ImageResizer.ResizeFile(_image.FullPath, width, height);
        }
        catch (IOException ex)
        {
            _statusBar.ShowMessage(Strings.Format("ImageTab_SaveFailed", ex.Message), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return;
        }

        if (!ok)
        {
            _statusBar.ShowMessage(Strings.Get("ImageTab_ResizeFailed"), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return;
        }

        if (_book is not null)
        {
            _book.Modified = true;
        }

        _image.NotifyUpdatedOnDisk();
        _statusBar.ShowMessage(Strings.Format("ImageTab_Resized", width, height), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc />
    public override void Reload() => LoadMetadata();

    private void OnResourceUpdatedOnDisk(object? sender, EventArgs e) => LoadMetadata();

    private void LoadMetadata()
    {
        _dimensions = _image.GetDimensions();
        _dpi = _image.GetDpi();
        _fileSize = _image.FileSize;

        _bitmap?.Dispose();
        _bitmap = TryLoadBitmap();

        OnPropertyChanged(nameof(Bitmap));
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
        OnPropertyChanged(nameof(InfoLine));
    }

    private Bitmap? TryLoadBitmap()
    {
        try
        {
            using FileStream stream = File.OpenRead(_image.FullPath);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // An unreadable file or no graphics platform (test context) — the preview
            // is optional; the info line and "Resize" work without the bitmap.
            return null;
        }
    }
}
