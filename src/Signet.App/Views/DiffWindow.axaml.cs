using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The "Compare" window — the differences between a checkpoint and the book's current state.
/// A non-modal window.
/// </summary>
public partial class DiffWindow : Window
{
    private DiffViewModel? _viewModel;

    /// <summary>Initializes the window.</summary>
    public DiffWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => Attach(null);
    }

    /// <summary>Creates and shows the window for the model (non-modally, above <paramref name="owner"/>).</summary>
    public static void ShowFor(Window owner, DiffViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        DiffWindow window = new() { DataContext = viewModel };
        MainWindow.RememberPlacement(window);
        window.Show(owner);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => Attach(DataContext as DiffViewModel);

    private void Attach(DiffViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        LoadImages();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiffViewModel.SelectedRowIndex):
                ScrollToSelectedRow();
                break;
            case nameof(DiffViewModel.LeftImagePath):
            case nameof(DiffViewModel.RightImagePath):
                LoadImages();
                break;
        }
    }

    private void ScrollToSelectedRow()
    {
        if (_viewModel is not { SelectedRowIndex: >= 0 } vm)
        {
            return;
        }

        ListBox list = vm.IsUnified ? UnifiedList : SideBySideList;
        list.ScrollIntoView(vm.SelectedRowIndex);
    }

    private void LoadImages()
    {
        LeftImage.Source = LoadBitmap(_viewModel?.LeftImagePath);
        RightImage.Source = LoadBitmap(_viewModel?.RightImagePath);
    }

    private static Bitmap? LoadBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is not null && sender is ListBox list)
        {
            _viewModel.ActivateRow(list.SelectedIndex);
        }
    }
}
