using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Services;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Accessibility" dialog (<see cref="AccessibilityViewModel"/>): three tabs — the language of the book and its files, the
/// descriptions of its images (a double click on an image opens its place in the code) and the accessibility metadata. "Save" writes
/// everything and closes the dialog; closing with unsaved changes asks first.
/// </summary>
public partial class AccessibilityWindow : Window
{
    private AccessibilityViewModel? _bound;
    private bool _forceClose;

    /// <summary>Initializes the window.</summary>
    public AccessibilityWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) =>
        {
            Unbind();
            (DataContext as AccessibilityViewModel)?.Dispose();
        };
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>.</summary>
    public static Task ShowAsync(Window owner, AccessibilityViewModel viewModel) =>
        new AccessibilityWindow { DataContext = viewModel }.ShowDialog(owner);

    private void Bind()
    {
        Unbind();
        _bound = DataContext as AccessibilityViewModel;
        if (_bound is not null)
        {
            _bound.Done += OnDone;
        }
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.Done -= OnDone;
            _bound = null;
        }
    }

    private void OnDone(object? sender, EventArgs e)
    {
        _forceClose = true;
        Close();
    }

    // "Close" and the title bar's X: with unsaved changes the user is asked first (Save, Discard or stay).
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose || _bound is not { HasUnsavedChanges: true })
        {
            return;
        }

        e.Cancel = true;
        _ = AskBeforeClosingAsync();
    }

    private async Task AskBeforeClosingAsync()
    {
        switch (await SaveChangesDialog.AskAsync(this))
        {
            case SaveChangesChoice.Save:
                if (_bound is { } vm)
                {
                    await vm.SaveCommand.ExecuteAsync(null);
                }

                break;
            case SaveChangesChoice.Discard:
                _forceClose = true;
                Close();
                break;
        }
    }

    private void OnImageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound is { } vm && ImageList.SelectedItem is AccessibilityImageItem item
            && e.Source is Control { DataContext: AccessibilityImageItem })
        {
            vm.OpenImage(item);
        }
    }
}
