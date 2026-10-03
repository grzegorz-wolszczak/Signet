using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// The "Resize Image" window: width / height fields and "Keep proportions".
/// Returns the target dimensions, or <c>null</c> after cancelling.
/// </summary>
public partial class ImageResizeWindow : Window
{
    private decimal _ratio = 1m;
    private bool _syncing;

    /// <summary>Initializes the window.</summary>
    public ImageResizeWindow()
    {
        InitializeComponent();

        WidthBox.ValueChanged += (_, _) => Sync(fromWidth: true);
        HeightBox.ValueChanged += (_, _) => Sync(fromWidth: false);
        OkButton.Click += (_, _) => Close(Result());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window; returns the target dimensions, or <c>null</c>.</summary>
    public static async Task<(int Width, int Height)?> AskAsync(Window owner, int initialWidth, int initialHeight)
    {
        ImageResizeWindow window = new();
        window._ratio = initialHeight > 0 ? (decimal)initialWidth / initialHeight : 1m;
        window.WidthBox.Value = initialWidth;
        window.HeightBox.Value = initialHeight;
        return await window.ShowDialog<(int Width, int Height)?>(owner);
    }

    private void Sync(bool fromWidth)
    {
        if (_syncing || KeepAspect.IsChecked != true || _ratio <= 0)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (fromWidth && WidthBox.Value is { } w)
            {
                HeightBox.Value = Math.Max(1m, Math.Round(w / _ratio, MidpointRounding.AwayFromZero));
            }
            else if (!fromWidth && HeightBox.Value is { } h)
            {
                WidthBox.Value = Math.Max(1m, Math.Round(h * _ratio, MidpointRounding.AwayFromZero));
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private (int Width, int Height)? Result()
    {
        int width = (int)(WidthBox.Value ?? 0m);
        int height = (int)(HeightBox.Value ?? 0m);
        return width >= 1 && height >= 1 ? (width, height) : null;
    }
}
