using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Clip Editor: a "*" in the title while there are unsaved changes, and a Save / Discard / Cancel
/// prompt when the window is closed.
/// </summary>
public sealed class ClipEditorCloseTests : IDisposable
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

    private (ClipEditorWindow Window, ClipsViewModel ViewModel, ClipStore Store) ShowModifiedEditor()
    {
        ClipStore store = new(_disposables.Add(new TempDir("signet-clipclose-")).Combine("file.xml"));
        ClipsViewModel vm = new(store, () => null, () => null, new StatusBarService());
        ClipEditorWindow window = new() { DataContext = vm };
        window.Show();
        Render(window);

        vm.AddEntryCommand.Execute(null);
        Render(window);
        return (window, vm, store);
    }

    private static SaveChangesDialog CloseAndGetPrompt(ClipEditorWindow window)
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

    [AvaloniaFact]
    public void Title_shows_an_asterisk_only_while_there_are_unsaved_changes()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm, _) = ShowModifiedEditor();
        string title = Strings.Get("ClipEditorWindow_Title");

        // Assert
        window.Title.Should().Be(title + "*");

        // Act
        vm.SaveCommand.Execute(null);
        Render(window);

        // Assert
        window.Title.Should().Be(title);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_with_unsaved_changes_asks_and_Cancel_keeps_the_window_open()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm, _) = ShowModifiedEditor();

        SaveChangesDialog prompt = CloseAndGetPrompt(window);

        // Assert
        prompt.FindControl<TextBlock>("MessageText")!.Text.Should().Be(Strings.Get("ClipEditor_SaveChangesPrompt"));

        // Act
        Press(prompt, "CancelButton");

        // Assert
        window.IsVisible.Should().BeTrue();
        vm.IsDataModified.Should().BeTrue();

        // Act
        Press(CloseAndGetPrompt(window), "DiscardButton");
    }

    [AvaloniaFact]
    public void Discard_closes_the_window_and_restores_the_saved_library()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm, ClipStore store) = ShowModifiedEditor();

        Press(CloseAndGetPrompt(window), "DiscardButton");

        // Assert
        window.IsVisible.Should().BeFalse();
        vm.Nodes.Should().BeEmpty();
        vm.IsDataModified.Should().BeFalse();
        store.Load().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Save_closes_the_window_and_writes_the_library_to_disk()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm, ClipStore store) = ShowModifiedEditor();

        Press(CloseAndGetPrompt(window), "SaveButton");

        // Assert
        window.IsVisible.Should().BeFalse();
        vm.IsDataModified.Should().BeFalse();
        store.Load().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Closing_without_changes_does_not_ask()
    {
        // Arrange
        ClipStore store = new(_disposables.Add(new TempDir("signet-clipclose-")).Combine("file.xml"));
        ClipEditorWindow window = new() { DataContext = new ClipsViewModel(store, () => null, () => null, new StatusBarService()) };

        // Act
        window.Show();
        Render(window);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        window.IsVisible.Should().BeFalse();
        window.OwnedWindows.Should().BeEmpty();
    }
    [AvaloniaFact]
    public void Exiting_the_app_with_unsaved_clips_asks_and_Cancel_aborts_the_exit()
    {
        // Arrange
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());
        MainWindow main = new() { DataContext = vm };

        // Act
        main.Show();
        Dispatcher.UIThread.RunJobs();
        vm.Clips.AddEntryCommand.Execute(null);

        main.Close();
        Dispatcher.UIThread.RunJobs();
        Press(main.OwnedWindows.OfType<SaveChangesDialog>().Single(), "CancelButton");

        // Assert
        main.IsVisible.Should().BeTrue();
        vm.Clips.IsDataModified.Should().BeTrue();

        // Act
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Press(main.OwnedWindows.OfType<SaveChangesDialog>().Single(), "DiscardButton");

        // Assert
        main.IsVisible.Should().BeFalse();
        vm.Clips.IsDataModified.Should().BeFalse();
    }
}
