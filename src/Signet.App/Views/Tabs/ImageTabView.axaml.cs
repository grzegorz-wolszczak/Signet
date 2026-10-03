using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Signet.App.ViewModels.Tabs;

namespace Signet.App.Views.Tabs;

/// <summary>
/// Image preview tab view: preview in a <c>ScrollViewer</c>, Ctrl+wheel zoom, an info line and a
/// "Resize Image" button that opens <see cref="ImageResizeWindow"/>.
/// </summary>
public partial class ImageTabView : UserControl
{
    private ImageTabViewModel? _boundViewModel;
    private double _zoom = 1.0;

    /// <summary>Initializes the view.</summary>
    public ImageTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ResizeButton.Click += OnResizeButtonClick;
        Scroller.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.ResizeRequested -= OnResizeRequested;
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = DataContext as ImageTabViewModel;
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.ResizeRequested += OnResizeRequested;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _zoom = 1.0;
        ApplyZoom();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageTabViewModel.Bitmap))
        {
            ApplyZoom();
        }
    }

    private void OnResizeButtonClick(object? sender, RoutedEventArgs e) => _boundViewModel?.RequestResize();

    private async void OnResizeRequested(object? sender, EventArgs e)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || _boundViewModel is null)
        {
            return;
        }

        int width = _boundViewModel.PixelWidth;
        int height = _boundViewModel.PixelHeight;
        if (width < 1 || height < 1)
        {
            return;
        }

        (int Width, int Height)? result = await ImageResizeWindow.AskAsync(owner, width, height);
        if (result is { } size)
        {
            _boundViewModel.ApplyResize(size.Width, size.Height);
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        double step = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _zoom = Math.Clamp(_zoom * step, 0.1, 8.0);
        ApplyZoom();
        e.Handled = true;
    }

    private void ApplyZoom()
    {
        if (_boundViewModel?.Bitmap is not Bitmap bitmap)
        {
            Preview.Width = double.NaN;
            Preview.Height = double.NaN;
            return;
        }

        Preview.Width = bitmap.Size.Width * _zoom;
        Preview.Height = bitmap.Size.Height * _zoom;
    }
}
