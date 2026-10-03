using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;

namespace Signet.App.Views.Tabs;

/// <summary>Audio / video / PDF tab view: information + "Open externally".</summary>
public partial class MediaTabView : UserControl
{
    private MediaTabViewModel? _boundViewModel;

    /// <summary>Initializes the view.</summary>
    public MediaTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        OpenExternallyButton.Click += OnOpenExternallyClick;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.OpenExternallyRequested -= OnOpenExternallyRequested;
        }

        _boundViewModel = DataContext as MediaTabViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.OpenExternallyRequested += OnOpenExternallyRequested;
        }
    }

    private void OnOpenExternallyClick(object? sender, RoutedEventArgs e) =>
        _boundViewModel?.RequestOpenExternally();

    private void OnOpenExternallyRequested(object? sender, EventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        ExternalOpen.TryOpen(_boundViewModel.FullPath, out _);
    }
}
