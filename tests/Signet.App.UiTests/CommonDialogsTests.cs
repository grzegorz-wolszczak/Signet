using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Views;
using Signet.Core;

namespace Signet.App.UiTests;

/// <summary>
/// Headless smoke tests of the common dialogs (Select Files / Select Folder / Clipboard History
/// Selector): the windows render without exceptions for representative data, in every
/// <see cref="SelectFilesMode"/>. The windows are built with <c>Build</c> (without the blocking
/// <c>ShowDialog</c>) and shown directly, as in <c>WindowRenderTests</c>/<c>NonTextTabsRenderTests</c>.
/// </summary>
public sealed class CommonDialogsTests
{
    private static readonly string[] TwoWarnings = { "first warning", "second warning" };

    private static void ShowAndRender(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void LoadWarningsDialog_lists_every_warning_with_a_localized_header()
    {
        LoadWarningsDialog window = LoadWarningsDialog.Build("book.epub", TwoWarnings);
        ShowAndRender(window);

        string?[] texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        TextBox list = window.GetVisualDescendants().OfType<TextBox>().Single();
        string?[] buttons = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToArray();

        window.Title.Should().Be(Strings.Get("LoadWarningsDialog_Title"));
        texts.Should().Contain(Strings.Format("LoadWarningsDialog_Header", "book.epub", 2));
        list.IsReadOnly.Should().BeTrue();
        list.Text.Should().Be(LoadWarningsDialog.FormatList(TwoWarnings));
        buttons.Should().Contain(Strings.Get("LoadWarningsDialog_Copy"));
    }

    [AvaloniaFact]
    public void SelectFilesWindow_SingleFileMode_renders_with_thumbnail_list()
    {
        SelectFilesEntry[] entries =
        {
            new("OEBPS/Images/cover.png", "cover.png", ResourceType.Image, null),
            new("OEBPS/Images/figure.png", "figure.png", ResourceType.Image, null),
        };

        SelectFilesWindow window = SelectFilesWindow.Build(
            "Add Cover", "Wybierz obraz:", SelectFilesMode.SingleFile, entries,
            allowInsertFromDisk: true, defaultSelectedBookPath: entries[0].BookPath);

        ShowAndRender(window);

        window.GetVisualDescendants().OfType<ListBox>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void SelectFilesWindow_MultipleMode_withTypeFilter_renders()
    {
        SelectFilesEntry[] entries =
        {
            new("OEBPS/Images/figure.png", "figure.png", ResourceType.Image, null),
            new("OEBPS/Audio/clip.mp3", "clip.mp3", ResourceType.Audio, null),
        };

        SelectFilesWindow window = SelectFilesWindow.Build(
            "Insert File", "Wybierz pliki do wstawienia:", SelectFilesMode.Multiple, entries,
            allowInsertFromDisk: true, showTypeFilter: true);

        ShowAndRender(window);

        window.GetVisualDescendants().OfType<ListBox>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void SelectFilesWindow_MultipleOrderedMode_renders_checkbox_rows_and_reorder_buttons()
    {
        SelectFilesEntry[] entries =
        {
            new("OEBPS/Styles/a.css", "OEBPS/Styles/a.css", ResourceType.Css, null, InitiallyIncluded: true),
            new("OEBPS/Styles/b.css", "OEBPS/Styles/b.css", ResourceType.Css, null),
        };

        SelectFilesWindow window = SelectFilesWindow.Build(
            "Link Stylesheets", "Arkusze stylów:", SelectFilesMode.MultipleOrdered, entries);

        ShowAndRender(window);

        window.GetVisualDescendants().OfType<CheckBox>().Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void SelectFolderWindow_renders_with_folder_suggestions()
    {
        string[] folders = { "OEBPS/Images", "OEBPS/Images/sub" };
        SelectFolderWindow window = SelectFolderWindow.Build(folders, "OEBPS/Images");

        ShowAndRender(window);

        window.GetVisualDescendants().OfType<AutoCompleteBox>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void ClipboardHistorySelectorWindow_renders_with_history_entries()
    {
        string[] history = { "first entry", "second entry" };
        ClipboardHistorySelectorWindow window = ClipboardHistorySelectorWindow.Build(history);

        ShowAndRender(window);

        window.GetVisualDescendants().OfType<ListBox>().Should().NotBeEmpty();
    }
}
