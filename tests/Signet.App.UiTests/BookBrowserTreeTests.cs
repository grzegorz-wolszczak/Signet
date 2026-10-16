using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The Book Browser tree (a TreeDataGrid): the optional "#" / Semantics / Properties columns switched from the header
/// menu and remembered, their values, and the multi-selection reaching the view model.
/// </summary>
public sealed class BookBrowserTreeTests
{
    private static void Render(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static (Window Window, BookBrowserViewModel Vm, BookBrowserView View, TreeDataGrid Tree) Show(SettingsStore settings, Book book)
    {
        BookBrowserViewModel vm = new(settings, new StatusBarService());
        vm.SetBook(book);
        BookBrowserView view = new() { DataContext = vm };
        Window window = new() { Width = 700, Height = 600, Content = view };
        window.Show();
        Render(window);
        return (window, vm, view, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    private static string?[] Headers(TreeDataGrid tree) =>
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Where(h => h.IsVisible)
            .OrderBy(h => h.ColumnIndex).Select(h => h.Header as string).ToArray();

    [AvaloniaFact]
    public void Only_the_name_is_shown_at_first_and_the_optional_columns_are_switched_and_remembered()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        (Window window, BookBrowserViewModel vm, BookBrowserView view, TreeDataGrid tree) = Show(settings, book);
        Headers(tree).Should().Equal(Strings.Get("ReportsWindow_Name"));
        tree.CanUserSortColumns.Should().BeFalse("the order of the files is the book's");

        view.ToggleOptionalColumn("Properties");
        view.ToggleOptionalColumn("ReadingOrder");
        Render(window);

        Headers(tree).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("BookBrowserView_ColumnOrderHeader"),
            Strings.Get("BookBrowserView_ColumnProperties"));
        settings.BookBrowserColumns.Should().Be("ReadingOrder,Properties");
        BookBrowserNode[] text = vm.Nodes.Single(n => n.Header == "Text").Children.ToArray();
        text.Select(n => n.ReadingOrderNumber).Should().Contain(1).And.Contain(2);
        vm.Nodes.SelectMany(n => n.Children).Single(n => n.Entry?.Resource.Filename == "nav.xhtml").PropertiesText.Should().Be("nav");
        window.Close();

        // A new panel shows the remembered columns; switching one off again hides it.
        (Window again, _, BookBrowserView view2, TreeDataGrid tree2) = Show(settings, book);
        Headers(tree2).Should().HaveCount(3);
        view2.ToggleOptionalColumn("ReadingOrder");
        Render(again);
        Headers(tree2).Should().Equal(Strings.Get("ReportsWindow_Name"), Strings.Get("BookBrowserView_ColumnProperties"));
        settings.BookBrowserColumns.Should().Be("Properties");
        again.Close();
    }

    [AvaloniaFact]
    public void The_look_settings_reach_the_tree_the_fonts_the_headers_the_indentation_and_the_row_spacing()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        (Window window, BookBrowserViewModel vm, BookBrowserView view, TreeDataGrid tree) = Show(settings, book);
        view.ToggleOptionalColumn("Properties");
        BookBrowserNode text = vm.Nodes.Single(n => n.Header == "Text");
        text.IsExpanded = true;
        Render(window);
        TreeDataGridExpanderCell[] Expanders() => tree.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().ToArray();
        TreeDataGridColumnHeader[] HeaderControls() => tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Where(h => h.IsVisible).ToArray();
        double defaultCellSize = Expanders()[0].FontSize;
        double defaultHeaderSize = HeaderControls()[0].FontSize;
        double[] defaultGaps = RowGaps(tree);

        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 20);
        defaultGaps.Should().OnlyContain(g => g == 3, "the rows are 3 px apart by default");

        // 0 means no space at all: a row is exactly as high as its content (no minimum height, no padding of a cell).
        settings.BookBrowserAppearance = BookBrowserAppearance.Default with { RowSpacing = 0 };
        Render(window);
        RowGaps(tree).Should().OnlyContain(g => g == 0);
        TreeDataGridRow[] rows = tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(r => r.IsVisible).ToArray();
        rows.Should().NotBeEmpty();
        foreach (TreeDataGridRow row in rows)
        {
            double content = row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Bounds.Height > 0)
                .Select(t => t.Bounds.Height).DefaultIfEmpty(0).Max();
            row.Bounds.Height.Should().BeLessThanOrEqualTo(Math.Max(content, 16) + 0.5, "nothing but the content makes a row high");
        }

        settings.BookBrowserAppearance = new BookBrowserAppearance(string.Empty, 20, string.Empty, 9, 35, 6);
        Render(window);

        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 35);
        Expanders().Should().OnlyContain(c => c.FontSize == 20);
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Should().OnlyContain(c => c.FontSize == 20, "the other columns follow the names");
        HeaderControls().Should().OnlyContain(h => h.FontSize == 9, "the headers have a font of their own");
        RowGaps(tree).Should().OnlyContain(g => g == 6);

        settings.BookBrowserAppearance = BookBrowserAppearance.Default;
        Render(window);

        Expanders()[0].FontSize.Should().Be(defaultCellSize, "a font left to the interface is not set");
        HeaderControls()[0].FontSize.Should().Be(defaultHeaderSize);
        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 20);
        RowGaps(tree).Should().OnlyContain(g => g == 3);
        window.Close();
    }

    // The distances between a row and the next one (the realized rows, top to bottom).
    private static double[] RowGaps(TreeDataGrid tree)
    {
        TreeDataGridRow[] rows = tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(r => r.IsVisible)
            .OrderBy(r => r.Bounds.Y).ToArray();
        return rows.Zip(rows.Skip(1), (a, b) => b.Bounds.Y - (a.Bounds.Y + a.Bounds.Height)).ToArray();
    }

    [AvaloniaFact]
    public void A_right_click_on_a_header_opens_the_columns_menu()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        (Window window, _, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        TreeDataGridColumnHeader header = tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().First(h => h.IsVisible);

        header.RaiseEvent(new ContextRequestedEventArgs());
        Render(window);

        MenuItem[] items = TopLevel.GetTopLevel(header)!.GetVisualDescendants().OfType<MenuItem>().ToArray();
        items.Select(i => i.Header).Should().Contain(Strings.Get("BookBrowserView_ColumnReadingOrder"))
            .And.Contain(Strings.Get("BookBrowserView_ColumnSemantics"))
            .And.Contain(Strings.Get("BookBrowserView_ColumnProperties"))
            .And.NotContain(Strings.Get("BookBrowserView_Open"), "the header menu is not the files menu");
        window.Close();
    }

    [AvaloniaFact]
    public void Several_selected_files_reach_the_view_model()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        BookBrowserNode textFolder = vm.Nodes.Single(n => n.Header == "Text");
        int folder = vm.Nodes.IndexOf(textFolder);

        tree.RowSelection!.Select(new IndexPath(folder, 0));
        tree.RowSelection.Select(new IndexPath(folder, 1));
        Render(window);

        vm.SelectedResources.Select(r => r.Filename).Should().Equal(
            textFolder.Children[0].Entry!.Resource.Filename, textFolder.Children[1].Entry!.Resource.Filename);
        window.Close();
    }
}
