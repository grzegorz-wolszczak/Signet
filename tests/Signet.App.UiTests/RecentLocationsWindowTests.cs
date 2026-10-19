using System;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The Recent Locations popup: renders the snippets, keys move the selection, forget an entry and close it.</summary>
public sealed class RecentLocationsWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (RecentLocationsWindow Window, RecentLocationsViewModel ViewModel, Book Book, TempDir Temp) Show()
    {
        TempDir temp = _disposables.Add(new TempDir());
        Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook());
        NavigationHistory history = new();
        foreach (HtmlResource html in book.GetHtmlResourcesExcludingNav())
        {
            html.InitialLoad();
            history.Push(new NavigationPlace(html.BookPath, 0, 1, DateTimeOffset.Now));
        }

        RecentLocationsViewModel vm = new(
            history,
            p => book.GetFolderKeeper().GetResourceByBookPathNoThrow(p),
            p => (book.GetFolderKeeper().GetResourceByBookPathNoThrow(p) as TextResource)?.GetText(),
            _ => { },
            25,
            string.Empty);
        RecentLocationsWindow window = new(new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift)) { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return (window, vm, book, temp);
    }

    [AvaloniaFact]
    public void The_entries_show_their_snippets()
    {
        // Act
        (RecentLocationsWindow window, RecentLocationsViewModel vm, Book book, TempDir temp) = Show();

        LocationSnippetEditor[] snippets = window.GetVisualDescendants().OfType<LocationSnippetEditor>().ToArray();

        // Assert
        snippets.Should().HaveCount(vm.Items.Count);
        snippets.Should().OnlyContain(s => s.Document.TextLength > 0 && s.Bounds.Height > 0);
        window.Close();
        book.Dispose();
        temp.Dispose();
    }

    [AvaloniaFact]
    public void Keys_move_the_selection_toggle_edited_only_forget_and_close()
    {
        // Act
        (RecentLocationsWindow window, RecentLocationsViewModel vm, Book book, TempDir temp) = Show();
        int count = vm.Items.Count;

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        // Assert
        vm.Selected.Should().BeSameAs(vm.Items[1]);

        // Act
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        // Assert
        vm.Items.Should().HaveCount(count - 1);

        // Act
        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert
        vm.ShowEditedOnly.Should().BeTrue();

        // Act
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        // Assert
        window.IsVisible.Should().BeFalse();
        book.Dispose();
        temp.Dispose();
    }
}
