using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Moq;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The "Saved Searches" window: closing it while the Add/Edit view holds an unsaved entry asks to save it first
/// (Save / Discard / Cancel), so the window can never reopen mid-edit — it always comes back to the list. It is
/// also recreated (not a true singleton) each time it is reopened after being closed, sharing one long-lived view
/// model across instances, so closing it must leave the old instance's view fully unsubscribed from that view
/// model (see <see cref="SearchEditorWindow.OnClosed"/>).
/// </summary>
public sealed class SearchEditorWindowCloseTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private (SearchEditorWindow Window, SearchEditorViewModel Vm, SavedSearchStore Store) Show(bool editing)
    {
        TempDir temp = _disposables.Add(new TempDir("signet-searchclose-"));
        SavedSearchStore store = new(temp.Combine("searches.xml"));
        SettingsStore settings = new(temp.Combine("settings.xml"));
        FindReplaceViewModel findReplace = new(
            settings, new StatusBarService(), () => null, new Mock<IMultiFileSearchHost>().Object);
        FindReplaceViewModel editFindReplace = new(
            settings, new StatusBarService(), () => null, new Mock<IMultiFileSearchHost>().Object);
        SearchEditorViewModel vm = new(store, settings, findReplace, editFindReplace);
        SearchEditorWindow window = new() { DataContext = vm };
        window.Show();
        Render(window);

        if (editing)
        {
            vm.AddEntryCommand.Execute(null);
            vm.EditingName = "Unsaved";
            vm.FindReplace.FindText = "x";
            Render(window);
        }

        return (window, vm, store);
    }

    private static SaveChangesDialog CloseAndGetPrompt(SearchEditorWindow window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
        return window.OwnedWindows.OfType<SaveChangesDialog>().Single();
    }

    private static void Press(SaveChangesDialog dialog, string buttonName)
    {
        dialog.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(ConfirmWindow dialog, string buttonName)
    {
        dialog.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Closing_mid_edit_asks_and_Cancel_keeps_the_window_open_and_the_edit_intact()
    {
        // Act
        (SearchEditorWindow window, SearchEditorViewModel vm, _) = Show(editing: true);
        SaveChangesDialog prompt = CloseAndGetPrompt(window);

        // Assert
        prompt.FindControl<TextBlock>("MessageText")!.Text.Should().Be(Strings.Get("SearchEditorWindow_UnsavedSearchPrompt"));

        // Act
        Press(prompt, "CancelButton");

        // Assert
        window.IsVisible.Should().BeTrue();
        vm.IsEditing.Should().BeTrue("Cancel must not discard the in-progress entry");
        vm.EditingName.Should().Be("Unsaved");
        window.Close(); // clean up: discard this time
        Press(window.OwnedWindows.OfType<SaveChangesDialog>().Single(), "DiscardButton");
    }

    [AvaloniaFact]
    public void Discard_closes_the_window_without_adding_the_entry_and_returns_to_the_list()
    {
        // Act
        (SearchEditorWindow window, SearchEditorViewModel vm, _) = Show(editing: true);
        Press(CloseAndGetPrompt(window), "DiscardButton");

        // Assert
        window.IsVisible.Should().BeFalse();
        vm.IsEditing.Should().BeFalse("so the window starts on the list, not mid-edit, the next time it is shown");
        vm.Nodes.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Save_closes_the_window_adds_the_entry_and_returns_to_the_list()
    {
        // Act
        (SearchEditorWindow window, SearchEditorViewModel vm, _) = Show(editing: true);
        Press(CloseAndGetPrompt(window), "SaveButton");

        // Assert
        window.IsVisible.Should().BeFalse();
        vm.IsEditing.Should().BeFalse("so the window starts on the list, not mid-edit, the next time it is shown");
        vm.Nodes.Should().ContainSingle(n => n.Name == "Unsaved" && n.Find == "x");
    }

    [AvaloniaFact]
    public void Closing_while_on_the_list_does_not_ask()
    {
        // Act
        (SearchEditorWindow window, SearchEditorViewModel vm, _) = Show(editing: false);
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        window.IsVisible.Should().BeFalse();
        window.OwnedWindows.Should().BeEmpty();
        vm.IsEditing.Should().BeFalse();
    }

    // Regression: the window is recreated (not a true singleton) each time it is reopened after being closed, but
    // its view model is one long-lived instance shared across those instances (MainWindow.axaml.cs's
    // OnSearchEditorRequested: "new SearchEditorWindow { DataContext = vm.SearchEditor }"). Before SearchEditorWindow
    // cleared the view's DataContext on close, the OLD, closed window's SearchEditorView stayed subscribed to the
    // view model's DeleteConfirmRequested — so deleting something in a REOPENED window fired both the new and the
    // stale handler, and the stale one tried to show a ConfirmWindow owned by the already-closed window and crashed
    // ("Cannot show a window with a closed owner").
    [AvaloniaFact]
    public void Deleting_in_a_reopened_window_does_not_also_fire_the_closed_windows_handler()
    {
        // Arrange
        (SearchEditorWindow first, SearchEditorViewModel vm, _) = Show(editing: false);
        AddEntry(vm, "First");
        vm.SetSelectedNodes(new[] { vm.Nodes.Single(n => n.Name == "First") });
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Press(first.OwnedWindows.OfType<ConfirmWindow>().Single(), "OkButton");
        first.Close();
        Dispatcher.UIThread.RunJobs();

        SearchEditorWindow reopened = new() { DataContext = vm };
        reopened.Show();
        Render(reopened);
        AddEntry(vm, "Second");
        vm.SetSelectedNodes(new[] { vm.Nodes.Single(n => n.Name == "Second") });

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert: exactly one confirmation, owned by the reopened window — the old window's handler did not also fire.
        first.OwnedWindows.OfType<ConfirmWindow>().Should().BeEmpty();
        ConfirmWindow confirm = reopened.OwnedWindows.OfType<ConfirmWindow>().Single();
        Press(confirm, "OkButton");
        vm.Nodes.Should().BeEmpty();
        reopened.Close();
    }

    private static void AddEntry(SearchEditorViewModel vm, string name)
    {
        vm.AddEntryCommand.Execute(null);
        vm.EditingName = name;
        vm.FindReplace.FindText = "x";
        vm.DoneEditCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Reopening_after_a_prior_edit_was_saved_or_discarded_starts_on_the_list()
    {
        // Act: close mid-edit, discard — the window is reused (Show/Activate), so a fresh Window bound to the
        // same, now-reset view model must come up on the list, exactly like MainWindow.axaml.cs's OnSearchEditorRequested.
        (SearchEditorWindow first, SearchEditorViewModel vm, _) = Show(editing: true);
        Press(CloseAndGetPrompt(first), "DiscardButton");

        SearchEditorWindow reopened = new() { DataContext = vm };
        reopened.Show();
        Render(reopened);

        // Assert
        vm.IsEditing.Should().BeFalse();
        reopened.Close();
    }
}
