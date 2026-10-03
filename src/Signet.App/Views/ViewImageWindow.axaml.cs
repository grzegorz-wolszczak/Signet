using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Signet.App.Resources;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// Modeless "View Image" window opened from the Code View context menu: the image fitted to the
/// window, an info line and "Done".
/// </summary>
/// <remarks>
/// Raster images are shown via <see cref="Bitmap"/>; for SVG (no SVG renderer among the
/// dependencies) the window only shows a notice pointing to "Open Tab For Image".
/// </remarks>
public partial class ViewImageWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ViewImageWindow()
    {
        InitializeComponent();
        DoneButton.Click += (_, _) => Close();
        Closed += (_, _) => DisposeImage();
    }

    /// <summary>Shows the given image resource.</summary>
    public void ShowImage(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        DisposeImage();
        Title = $"{Strings.Get("ViewImageWindow_Title")} — {resource.Filename}";

        if (resource is SvgResource)
        {
            ShowFallback(Strings.Get("ViewImageWindow_SvgNotSupported"));
            InfoText.Text = resource.Filename;
            return;
        }

        Bitmap? bitmap = TryLoad(resource.FullPath);
        if (bitmap is null)
        {
            ShowFallback(Strings.Get("ViewImageWindow_CannotLoad"));
            InfoText.Text = resource.Filename;
            return;
        }

        FallbackText.IsVisible = false;
        ImageView.Source = bitmap;
        ImageView.IsVisible = true;
        long size = resource is ImageResource image ? image.FileSize : 0;
        InfoText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{resource.Filename} | {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} px | {(size + 512) / 1024} KB");
    }

    private void ShowFallback(string message)
    {
        ImageView.IsVisible = false;
        FallbackText.Text = message;
        FallbackText.IsVisible = true;
    }

    private void DisposeImage()
    {
        if (ImageView.Source is Bitmap old)
        {
            ImageView.Source = null;
            old.Dispose();
        }
    }

    private static Bitmap? TryLoad(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
#pragma warning disable CA1031 // Unreadable file — the window shows a message instead of crashing.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
