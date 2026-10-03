using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// In-place file rename in the Book Browser (an editor in the tree instead of a window) and the
/// light text selection color in edit fields.
/// </summary>
public sealed class BookBrowserInPlaceRenameTests
{
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, BookBrowserViewModel ViewModel, BookBrowserNode File) ShowBrowser(Book book)
    {
        SettingsStore settings = new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"signet-bbui-{System.Guid.NewGuid():N}.json"));
        BookBrowserViewModel vm = new(settings, new StatusBarService());
        vm.SetBook(book);

        Window window = new() { Width = 400, Height = 600, Content = new BookBrowserView { DataContext = vm } };
        window.Show();
        Render(window);

        BookBrowserNode file = vm.Nodes.Single(n => n.Header == "Text").Children[0];
        window.GetVisualDescendants().OfType<TreeView>().Single().SelectedItems.Add(file);
        Render(window);
        return (window, vm, file);
    }

    private static TextBox Editor(Window window) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("inPlaceRename") && t.IsVisible);

    [AvaloniaFact]
    public void Rename_shows_a_focused_editor_in_the_tree_with_the_filename_selected()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        (Window window, BookBrowserViewModel vm, BookBrowserNode file) = ShowBrowser(book);

        vm.RenameSelectedCommand.Execute(null);
        Render(window);

        TextBox editor = Editor(window);
        editor.IsFocused.Should().BeTrue();
        editor.SelectedText.Should().Be(file.Entry!.Resource.Filename);
        window.OwnedWindows.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Enter_commits_and_Escape_cancels_the_in_place_rename()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        (Window window, BookBrowserViewModel vm, BookBrowserNode file) = ShowBrowser(book);
        Signet.Core.Resources.Resource resource = file.Entry!.Resource;
        string original = resource.Filename;

        vm.RenameSelectedCommand.Execute(null);
        Render(window);
        Editor(window).Text = "cancelled.xhtml";
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Render(window);

        resource.Filename.Should().Be(original);
        vm.EditingNode.Should().BeNull();

        vm.RenameSelectedCommand.Execute(null);
        Render(window);
        Editor(window).Text = "committed.xhtml";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Render(window);

        resource.Filename.Should().Be("committed.xhtml");
        vm.EditingNode.Should().BeNull();
    }

    [AvaloniaFact]
    public void Text_selection_in_a_TextBox_uses_a_light_brush_in_the_light_theme()
    {
        TextBox textBox = new() { Text = "Section0001.xhtml" };
        Window window = new() { RequestedThemeVariant = ThemeVariant.Light, Content = textBox };
        window.Show();
        Render(window);

        ((ISolidColorBrush)textBox.SelectionBrush!).Color.Should().Be(Color.Parse("#ADD6FF"));
        ((ISolidColorBrush)textBox.SelectionForegroundBrush!).Color.Should().Be(Colors.Black);
    }
}
