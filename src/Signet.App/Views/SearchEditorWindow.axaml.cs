using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// Modeless "Saved Searches" window (does not block the main window). A new instance replaces the old one each
/// time it is reopened after being closed (MainWindow.axaml.cs's OnSearchEditorRequested); Activate() is used
/// instead while one is already open. Its view model (<c>SearchEditorViewModel</c>) is a single, long-lived
/// instance shared across those instances, so <see cref="OnClosed"/> clears the view's <c>DataContext</c> to
/// unsubscribe from it — otherwise an old, closed instance would keep reacting to its events.
/// </summary>
public partial class SearchEditorWindow : Window
{
    private bool _forceClose;

    /// <summary>Initializes the window.</summary>
    public SearchEditorWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // When the application closes, MainWindow asks (before the child windows are closed). Closing with an
        // Add/Edit view open asks to save it first only when it actually differs from what it started from
        // (IsEditDirty) — opening Edit Search and closing again untouched is not a change. The window is also
        // reused across opens (Show/Activate), so either path leaves it back on the list for next time.
        if (!_forceClose && e.CloseReason != WindowCloseReason.OwnerWindowClosing
            && DataContext is SearchEditorViewModel { IsEditing: true } vm)
        {
            if (vm.IsEditDirty)
            {
                e.Cancel = true;
                _ = ConfirmAndCloseAsync(vm);
                return;
            }

            vm.CancelEditCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // The window is recreated (not truly reused) across opens (MainWindow.axaml.cs's OnSearchEditorRequested:
        // new SearchEditorWindow { DataContext = vm.SearchEditor }), but vm.SearchEditor itself is a single,
        // long-lived instance — so the old SearchEditorView's event subscriptions to it (ImportRequested,
        // ExportRequested, DeleteConfirmRequested, PropertyChanged) would otherwise outlive this window. Left in
        // place, the next Delete (say) would reach both the old and the new view's handlers; the old one would
        // resolve its owner to this now-closed window and crash trying to show a dialog on it. Clearing the
        // view's DataContext here runs its usual unsubscribe path (SearchEditorView.OnDataContextChanged).
        View.DataContext = null;
        base.OnClosed(e);
    }

    private async Task ConfirmAndCloseAsync(SearchEditorViewModel vm)
    {
        switch (await SaveChangesDialog.AskAsync(this, Strings.Get("SearchEditorWindow_UnsavedSearchPrompt")))
        {
            case SaveChangesChoice.Save:
                vm.DoneEditCommand.Execute(null);
                break;
            case SaveChangesChoice.Discard:
                vm.CancelEditCommand.Execute(null);
                break;
            default:
                return;
        }

        _forceClose = true;
        Close();
    }
}
