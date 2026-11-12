using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The Show Bookmarks popup (<see cref="BookmarksPopupViewModel"/>). Up / Down (PageUp / PageDown) move the selection,
/// Enter or a double click goes to the bookmark, Delete removes it. A single click only selects a bookmark; the popup closes on
/// Esc, on a click outside of it and after going to a bookmark.
/// </summary>
public partial class BookmarksPopupWindow : Window
{
    private const int PageStep = 5;

    private BookmarksPopupViewModel? _bound;
    private bool _closing;

    /// <summary>Initializes the window.</summary>
    public BookmarksPopupWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Bookmarks.AddHandler(DoubleTappedEvent, OnBookmarkDoubleTapped);
        Deactivated += (_, _) => CloseOnce();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_bound is not null)
        {
            _bound.PreviewChanged -= OnPreviewChanged;
            _bound.CloseRequested -= OnCloseRequested;
        }

        _bound = DataContext as BookmarksPopupViewModel;
        if (_bound is not null)
        {
            _bound.PreviewChanged += OnPreviewChanged;
            _bound.CloseRequested += OnCloseRequested;
            OnPreviewChanged(null, EventArgs.Empty);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => CloseOnce();

    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (_bound is not { Selected: not null } vm)
        {
            return;
        }

        Preview.ApplyFont(vm.Appearance);
        Preview.Show(vm.PreviewText, vm.PreviewSyntax, vm.PreviewLine, vm.BookmarkedLinesOfPreview);
        if (vm.Selected is { } selected)
        {
            Bookmarks.ScrollIntoView(selected);
        }
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_bound is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                CloseOnce();
                break;
            case Key.Up or Key.Down:
                vm.MoveSelection(e.Key == Key.Up ? -1 : 1);
                break;
            case Key.PageUp or Key.PageDown:
                vm.MoveSelection(e.Key == Key.PageUp ? -PageStep : PageStep);
                break;
            case Key.Enter when vm.Selected is { } selected:
                vm.Navigate(selected);
                break;
            case Key.Delete:
                vm.RemoveSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // A click only selects the bookmark (and shows its file); a double click goes to it, like Enter.
    private void OnBookmarkDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound is { Selected: { } selected } vm)
        {
            vm.Navigate(selected);
        }
    }

    private void CloseOnce()
    {
        if (!_closing)
        {
            _closing = true;
            Close();
        }
    }
}
