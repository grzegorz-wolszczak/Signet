using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// The Book Browser tree (a TreeDataGrid): the optional "#" / Semantics / Properties columns switched from the header
/// menu and remembered, their values, and the multi-selection reaching the view model.
/// </summary>
public sealed class BookBrowserTreeTests
{
    private sealed class SuspendingBookOperationHost : IBookOperationHost
    {
        private readonly BookBrowserViewModel _viewModel;

        public SuspendingBookOperationHost(BookBrowserViewModel viewModel) => _viewModel = viewModel;

        public Task<BookOperationOutcome<T>> RunAsync<T>(
            string title, string? checkpointName, Func<T> work, bool delayedProgressWindow = false)
        {
            _viewModel.SuspendRebuilds = true;
            try
            {
                return Task.FromResult(new BookOperationOutcome<T>(true, work(), false));
            }
            finally
            {
                _viewModel.SuspendRebuilds = false;
            }
        }
    }

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

    [AvaloniaFact]
    public async Task MoveDown_menu_command_reorders_visible_spine_rows()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) = Show(settings, book);
        vm.Operations = new SuspendingBookOperationHost(vm);

        try
        {
            BookBrowserNode textFolder = vm.Nodes.Single(node => node.Header == "Text");
            textFolder.IsExpanded = true;
            Render(window);

            BookBrowserNode[] before = textFolder.Children
                .Where(node => node.Entry!.ReadingOrder >= 0)
                .ToArray();
            before.Should().HaveCountGreaterThan(1);
            BookBrowserNode first = before[0];
            tree.RowSelection!.Select(new IndexPath(
                vm.Nodes.IndexOf(textFolder), textFolder.Children.IndexOf(first)));
            Render(window);

            ContextMenu menu = tree.ContextMenu!;
            menu.Close();
            menu.Open(tree);
            Render(window);
            MenuItem moveDown = menu.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.Get("BookBrowserView_MoveDown")));

            // Assert
            moveDown.IsEffectivelyEnabled.Should().BeTrue();
            moveDown.Command.Should().BeSameAs(vm.MoveTextDownCommand);

            // Act
            await vm.MoveTextDownCommand.ExecuteAsync(null);
            Render(window);

            // Assert
            string[] expected = before.Select(node => node.Header).ToArray();
            (expected[0], expected[1]) = (expected[1], expected[0]);
            BookBrowserNode reordered = vm.Nodes.Single(node => node.Header == "Text").Children
                .Single(node => ReferenceEquals(node.Entry?.Resource, first.Entry!.Resource));
            reordered.Should().BeSameAs(first);
            vm.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(first.Entry!.Resource);
            string[] visible = tree.GetVisualDescendants().OfType<TreeDataGridRow>()
                .Where(row => row.IsVisible)
                .OrderBy(row => row.RowIndex)
                .Select(row => row.Model)
                .OfType<BookBrowserNode>()
                .Where(node => node.Entry is { ReadingOrder: >= 0 })
                .Select(node => node.Header)
                .ToArray();
            visible.Should().Equal(expected);
            tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(row => row.IsVisible)
                .Should().Contain(row => ReferenceEquals(row.Model, first));
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Reordering_keeps_visible_selection_and_scroll_but_reveals_an_edge_move()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(SyntheticBook.BuildEpub(temp, files: 100)).GetBook();
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) =
            Show(new SettingsStore(temp.Combine("settings.xml")), book);
        vm.Operations = new SuspendingBookOperationHost(vm);

        try
        {
            BookBrowserNode textFolder = vm.Nodes.Single(node => node.Header == "Text");
            textFolder.IsExpanded = true;
            Render(window);

            int folderIndex = vm.Nodes.IndexOf(textFolder);
            int targetIndex = 50;
            int targetRow = tree.Rows!.ModelIndexToRowIndex(new IndexPath(folderIndex, targetIndex));
            tree.RowsPresenter!.BringIntoView(targetRow);
            Render(window);

            BookBrowserNode[] visibleNodes = tree.GetVisualDescendants().OfType<TreeDataGridRow>()
                .Where(row => row.IsVisible)
                .OrderBy(row => row.RowIndex)
                .Select(row => row.Model)
                .OfType<BookBrowserNode>()
                .Where(node => node.Entry is { ReadingOrder: >= 0 })
                .ToArray();
            visibleNodes.Should().NotBeEmpty();
            BookBrowserNode selected = visibleNodes[visibleNodes.Length / 2];
            int selectedIndex = textFolder.Children.IndexOf(selected);
            tree.RowSelection!.Select(new IndexPath(folderIndex, selectedIndex));
            Render(window);
            double offsetBeforeMove = tree.Scroll!.Offset.Y;
            offsetBeforeMove.Should().BeGreaterThan(0);

            // Act
            await vm.MoveTextDownCommand.ExecuteAsync(null);
            Render(window);

            // Assert
            textFolder.Children.IndexOf(selected).Should().Be(selectedIndex + 1);
            vm.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(selected.Entry!.Resource);
            tree.Scroll!.Offset.Y.Should().Be(offsetBeforeMove);
            tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(row => row.IsVisible)
                .Should().Contain(row => ReferenceEquals(row.Model, selected));

            double offsetBeforeEdgeMove = tree.Scroll.Offset.Y;

            // Act
            await vm.MoveTextToBottomCommand.ExecuteAsync(null);
            Render(window);

            // Assert
            textFolder.Children.Where(node => node.Entry is { ReadingOrder: >= 0 }).Last().Should().BeSameAs(selected);
            vm.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(selected.Entry!.Resource);
            tree.Scroll.Offset.Y.Should().BeGreaterThan(offsetBeforeEdgeMove);
            tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(row => row.IsVisible)
                .Should().Contain(row => ReferenceEquals(row.Model, selected));
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    // The selected rows dragged over `point` and dropped there, as the platform's drag loop would deliver it (the tree
    // starts that loop itself; the headless platform has none). Returns the effect the tree accepted over the point.
    private static DragDropEffects DragSelectionTo(Window window, TreeDataGrid tree, Point point)
    {
        DataTransfer data = new();
        data.Add(DataTransferItem.Create(
            DragInfo.DataFormat, new DragInfo(tree.Source!, tree.RowSelection!.SelectedIndexes.ToList())));
        DragDropEffects accepted = DragDropEffects.None;
        EventHandler<DragEventArgs> record = (_, e) => accepted = e.DragEffects;
        window.AddHandler(DragDrop.DragOverEvent, record, RoutingStrategies.Bubble, handledEventsToo: true);
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Move, RawInputModifiers.None);
        window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Move, RawInputModifiers.None);
        window.RemoveHandler(DragDrop.DragOverEvent, record);
        window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Move, RawInputModifiers.None);
        Render(window);
        return accepted;
    }

    private static Point InRow(TreeDataGrid tree, Window window, BookBrowserNode node, double heightFraction)
    {
        TreeDataGridRow row = tree.GetVisualDescendants().OfType<TreeDataGridRow>().Single(r => ReferenceEquals(r.Model, node));
        return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height * heightFraction), window)!.Value;
    }

    private static void SelectInFolder(TreeDataGrid tree, BookBrowserViewModel vm, BookBrowserNode folder, params BookBrowserNode[] nodes)
    {
        tree.RowSelection!.Clear();
        foreach (BookBrowserNode node in nodes)
        {
            tree.RowSelection.Select(new IndexPath(vm.Nodes.IndexOf(folder), folder.Children.IndexOf(node)));
        }
    }

    [AvaloniaTheory]
    [InlineData(0.9, true)]
    [InlineData(0.1, false)]
    public void Dropping_a_text_file_on_the_lower_or_upper_half_of_a_row_puts_it_after_or_before(
        double heightFraction, bool after)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(SyntheticBook.BuildEpub(temp, files: 5)).GetBook();
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) =
            Show(new SettingsStore(temp.Combine("settings.xml")), book);
        vm.Operations = new SuspendingBookOperationHost(vm);

        try
        {
            BookBrowserNode textFolder = vm.Nodes.Single(node => node.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Text);
            textFolder.IsExpanded = true;
            Render(window);
            BookBrowserNode[] before = textFolder.Children.ToArray();
            BookBrowserNode[] spine = before.Where(n => n.Entry is { ReadingOrder: >= 0 }).ToArray();
            BookBrowserNode dragged = spine[0];
            BookBrowserNode over = spine[3];
            SelectInFolder(tree, vm, textFolder, dragged);
            Render(window);

            // Act
            DragDropEffects accepted = DragSelectionTo(window, tree, InRow(tree, window, over, heightFraction));

            // Assert
            accepted.Should().Be(DragDropEffects.Move);
            BookBrowserNode[] expected = before.Where(n => n != dragged).ToArray();
            int overIndex = Array.IndexOf(expected, over);
            expected = expected.Take(after ? overIndex + 1 : overIndex).Append(dragged)
                .Concat(expected.Skip(after ? overIndex + 1 : overIndex)).ToArray();
            textFolder.Children.Should().Equal(expected, "the nodes are moved, not rebuilt");
            vm.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(dragged.Entry!.Resource);
            book.GetOpf().GetSpineOrderBookPaths().Should().Equal(
                expected.Where(n => spine.Contains(n)).Select(n => n.Entry!.BookPath));
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public void Dropping_a_text_file_where_it_already_is_or_outside_the_text_files_changes_nothing()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) =
            Show(new SettingsStore(temp.Combine("settings.xml")), book);
        vm.Operations = new SuspendingBookOperationHost(vm);

        try
        {
            BookBrowserNode textFolder = vm.Nodes.Single(node => node.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Text);
            BookBrowserNode stylesFolder = vm.Nodes.Single(node => node.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Styles);
            textFolder.IsExpanded = true;
            stylesFolder.IsExpanded = true;
            Render(window);
            BookBrowserNode[] before = textFolder.Children.ToArray();
            before.Should().HaveCountGreaterThan(1);
            string[] spineBefore = book.GetOpf().GetSpineOrderBookPaths().ToArray();
            SelectInFolder(tree, vm, textFolder, before[0]);
            Render(window);

            // Act
            DragDropEffects ontoItself = DragSelectionTo(window, tree, InRow(tree, window, before[0], 0.9));
            DragDropEffects ontoNextUpperHalf = DragSelectionTo(window, tree, InRow(tree, window, before[1], 0.1));
            DragDropEffects ontoStylesheet = DragSelectionTo(
                window, tree, InRow(tree, window, stylesFolder.Children.First(n => n.Entry is not null), 0.9));

            // Assert
            (ontoItself, ontoNextUpperHalf, ontoStylesheet).Should().Be(
                (DragDropEffects.None, DragDropEffects.None, DragDropEffects.None));
            textFolder.Children.Should().Equal(before);
            book.GetOpf().GetSpineOrderBookPaths().Should().Equal(spineBefore);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public void Several_selected_files_or_a_stylesheet_do_not_start_a_drag()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) =
            Show(new SettingsStore(temp.Combine("settings.xml")), book);

        try
        {
            BookBrowserNode textFolder = vm.Nodes.Single(node => node.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Text);
            BookBrowserNode stylesFolder = vm.Nodes.Single(node => node.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Styles);
            textFolder.IsExpanded = true;
            stylesFolder.IsExpanded = true;
            Render(window);
            BookBrowserNode[] texts = textFolder.Children.ToArray();
            BookBrowserNode stylesheet = stylesFolder.Children.First(n => n.Entry is not null);
            DragDropEffects? allowed = null;
            tree.RowDragStarted += (_, e) => allowed = e.AllowedEffects;

            // Act
            SelectInFolder(tree, vm, textFolder, texts[0], texts[^1]);
            Render(window);
            Point start = InRow(tree, window, texts[0], 0.5);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(start.X, start.Y + 30), MouseButton.Left);
            Render(window);

            // Assert
            allowed.Should().Be(DragDropEffects.None, "several files are selected");

            // Act
            allowed = null;
            SelectInFolder(tree, vm, stylesFolder, stylesheet);
            Render(window);
            start = InRow(tree, window, stylesheet, 0.5);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(start.X, start.Y + 30), MouseButton.Left);
            Render(window);

            // Assert
            allowed.Should().Be(DragDropEffects.None, "only a Text file has a place in the spine");
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    private static string?[] Headers(TreeDataGrid tree) =>
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Where(h => h.IsVisible)
            .OrderBy(h => h.ColumnIndex).Select(h => h.Header as string).ToArray();

    [AvaloniaFact]
    public void Only_the_name_is_shown_at_first_and_the_optional_columns_are_switched_and_remembered()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        SettingsStore settings = new(temp.Combine("settings.xml"));

        // Act
        (Window window, BookBrowserViewModel vm, BookBrowserView view, TreeDataGrid tree) = Show(settings, book);

        // Assert
        Headers(tree).Should().Equal(Strings.Get("ReportsWindow_Name"));
        tree.CanUserSortColumns.Should().BeFalse("the order of the files is the book's");

        // Act
        view.ToggleOptionalColumn("Properties");
        view.ToggleOptionalColumn("ReadingOrder");
        Render(window);

        // Assert
        Headers(tree).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("BookBrowserView_ColumnOrderHeader"),
            Strings.Get("BookBrowserView_ColumnProperties"));
        settings.BookBrowserColumns.Should().Be("ReadingOrder,Properties");
        BookBrowserNode[] text = vm.Nodes.Single(n => n.Header == "Text").Children.ToArray();
        text.Select(n => n.ReadingOrderNumber).Should().Contain(1).And.Contain(2);
        vm.Nodes.SelectMany(n => n.Children).Single(n => n.Entry?.Resource.Filename == "nav.xhtml").PropertiesText.Should().Be("nav");
        window.Close();

        // A new panel shows the remembered columns; switching one off again hides it.
        // Act
        (Window again, _, BookBrowserView view2, TreeDataGrid tree2) = Show(settings, book);

        // Assert
        Headers(tree2).Should().HaveCount(3);

        // Act
        view2.ToggleOptionalColumn("ReadingOrder");
        Render(again);

        // Assert
        Headers(tree2).Should().Equal(Strings.Get("ReportsWindow_Name"), Strings.Get("BookBrowserView_ColumnProperties"));
        settings.BookBrowserColumns.Should().Be("Properties");
        again.Close();
    }

    [AvaloniaFact]
    public void The_look_settings_reach_the_tree_the_fonts_the_headers_the_indentation_and_the_height_of_the_rows()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        SettingsStore settings = new(temp.Combine("settings.xml"));

        // Act
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

        // Assert
        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 20);
        defaultGaps.Should().OnlyContain(g => g == 0, "the rows are not apart: there is no spacing setting, the height of a cell does it");

        // By default a row is as high as its content (the icons), with no padding of a cell.
        // Act
        settings.BookBrowserAppearance = BookBrowserAppearance.Default;
        Render(window);

        // Assert
        RowGaps(tree).Should().OnlyContain(g => g == 0);
        TreeDataGridRow[] rows = tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(r => r.IsVisible).ToArray();
        rows.Should().NotBeEmpty();
        foreach (TreeDataGridRow row in rows)
        {
            double content = row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Bounds.Height > 0)
                .Select(t => t.Bounds.Height).DefaultIfEmpty(0).Max();
            row.Bounds.Height.Should().BeLessThanOrEqualTo(Math.Max(content, 16) + 0.5, "nothing but the content makes a row high");
        }

        // The minimum height of a cell and its vertical padding (the name cell and the text cells alike).
        double[] Heights() => tree.GetVisualDescendants().OfType<TreeDataGridRow>().Where(r => r.IsVisible)
            .Select(r => r.Bounds.Height).ToArray();
        double contentHeight = Heights().Max();

        // Act
        settings.BookBrowserAppearance = BookBrowserAppearance.Default with { CellMinHeight = 40 };
        Render(window);

        // Assert
        Heights().Should().OnlyContain(h => h == 40, "the minimum height of a cell is the height of a row");

        // Act
        settings.BookBrowserAppearance = BookBrowserAppearance.Default with { CellMinHeight = 0, CellPadding = 5 };
        Render(window);

        // Assert
        Heights().Should().OnlyContain(h => h >= 10 + 10, "the padding is above and below the content");
        Heights().Max().Should().BeApproximately(contentHeight + 10, 1.5);

        // The background comes from the theme.
        // Act
        Avalonia.Application.Current!.Resources["SignetBookBrowserBackgroundBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red);
        try
        {
            Render(window);

            // Assert
            ((Avalonia.Media.ISolidColorBrush)tree.Background!).Color.Should().Be(Avalonia.Media.Colors.Red);
        }
        finally
        {
            Avalonia.Application.Current.Resources.Remove("SignetBookBrowserBackgroundBrush");
        }

        // Act
        settings.BookBrowserAppearance = new BookBrowserAppearance(string.Empty, 20, string.Empty, 9, 35);
        Render(window);

        // Assert
        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 35);
        Expanders().Should().OnlyContain(c => c.FontSize == 20);
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Should().OnlyContain(c => c.FontSize == 20, "the other columns follow the names");
        HeaderControls().Should().OnlyContain(h => h.FontSize == 9, "the headers have a font of their own");

        // Act
        settings.BookBrowserAppearance = BookBrowserAppearance.Default;
        Render(window);

        // Assert
        Expanders()[0].FontSize.Should().Be(defaultCellSize, "a font left to the interface is not set");
        HeaderControls()[0].FontSize.Should().Be(defaultHeaderSize);
        Expanders().Where(c => c.Indent == 1).Select(c => c.IndentPadding.Left).Should().OnlyContain(p => p == 20);
        window.Close();
    }

    [AvaloniaFact]
    public void The_selected_file_takes_the_colors_of_the_theme_in_this_tree_only()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();

        // Act
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        vm.Nodes.Single(n => n.Header == "Text").IsExpanded = true;
        Render(window);
        const string brush = "TreeDataGridSelectedCellBackgroundBrush";

        // Assert
        tree.Resources.ContainsKey(brush).Should().BeFalse("nothing is set: the controls' selection color");
        Theme theme = new Theme("t", "T").With(ThemeKeys.UiBookBrowserSelection.Id, ThemeEntry.Of(new TextAttributes
        {
            Foreground = ThemeColor.Parse("#00FF00"),
            Background = ThemeColor.Parse("#FF0000"),
        }));
        try
        {
            // Act
            Avalonia.Application.Current!.Resources.Remove(brush);
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Of(ThemeDocumentKind.Theme, theme, new ThemeResolver(_ => null)));
            Render(window);

            // Assert
            ((Avalonia.Media.ISolidColorBrush)tree.Resources[brush]!).Color.Should().Be(Avalonia.Media.Colors.Red);
            Avalonia.Application.Current.Resources.ContainsKey(brush).Should().BeFalse("the selection color of the other controls is not touched");
        }
        finally
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Defaults);
        }

        // Act
        Render(window);

        // Assert
        tree.Resources.ContainsKey(brush).Should().BeFalse("the default theme sets nothing again");
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
    public void The_files_menu_has_the_add_actions_only_on_a_folder_and_enables_the_ones_that_fit_it()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

        // Act
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        string[] addHeaders =
        {
            Strings.Get("BookBrowserView_AddBlankHtml"), Strings.Get("BookBrowserView_AddBlankCss"),
            Strings.Get("BookBrowserView_AddBlankSvg"), Strings.Get("BookBrowserView_AddBlankJs"),
        };

        MenuItem[] Open()
        {
            tree.ContextMenu!.Close();
            tree.ContextMenu.Open(tree);
            Render(window);
            return tree.ContextMenu.Items.OfType<MenuItem>().ToArray();
        }

        vm.UpdateSelection(new object[] { vm.Nodes.Single(n => n.Header == "Styles") });
        MenuItem[] onFolder = Open();
        MenuItem Add(string key) => onFolder.Single(i => Equals(i.Header, Strings.Get(key)));

        // Assert
        onFolder.Where(i => addHeaders.Contains(i.Header as string)).Should().OnlyContain(i => i.IsVisible);
        Add("BookBrowserView_AddBlankCss").IsEffectivelyEnabled.Should().BeTrue();
        Add("BookBrowserView_AddBlankHtml").IsEffectivelyEnabled.Should().BeFalse("not for the Styles folder");
        Add("BookBrowserView_AddBlankSvg").IsEffectivelyEnabled.Should().BeFalse();
        Add("BookBrowserView_AddBlankJs").IsEffectivelyEnabled.Should().BeFalse();
        onFolder.Should().Contain(i => Equals(i.Header, Strings.Get("BookBrowserView_AddStylesheetFiles")) && i.IsEffectivelyEnabled);

        // Act
        vm.UpdateSelection(new object[] { vm.Nodes.Single(n => n.Header == "Text").Children[0] });
        MenuItem[] onFile = Open();

        // Assert
        onFile.Where(i => addHeaders.Contains(i.Header as string)).Should().OnlyContain(i => !i.IsVisible, "no add actions on a file");
        window.Close();
    }

    [AvaloniaFact]
    public void A_right_click_on_a_header_opens_the_columns_menu()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();

        // Act
        (Window window, _, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        TreeDataGridColumnHeader header = tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().First(h => h.IsVisible);

        header.RaiseEvent(new ContextRequestedEventArgs());
        Render(window);

        MenuItem[] items = TopLevel.GetTopLevel(header)!.GetVisualDescendants().OfType<MenuItem>().ToArray();

        // Assert
        items.Select(i => i.Header).Should().Contain(Strings.Get("BookBrowserView_ColumnReadingOrder"))
            .And.Contain(Strings.Get("BookBrowserView_ColumnSemantics"))
            .And.Contain(Strings.Get("BookBrowserView_ColumnProperties"))
            .And.NotContain(Strings.Get("BookBrowserView_Open"), "the header menu is not the files menu");
        window.Close();
    }

    [AvaloniaFact]
    public void Several_selected_files_reach_the_view_model()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();

        // Act
        (Window window, BookBrowserViewModel vm, _, TreeDataGrid tree) = Show(new SettingsStore(temp.Combine("settings.xml")), book);
        BookBrowserNode textFolder = vm.Nodes.Single(n => n.Header == "Text");
        int folder = vm.Nodes.IndexOf(textFolder);

        tree.RowSelection!.Select(new IndexPath(folder, 0));
        tree.RowSelection.Select(new IndexPath(folder, 1));
        Render(window);

        // Assert
        vm.SelectedResources.Select(r => r.Filename).Should().Equal(
            textFolder.Children[0].Entry!.Resource.Filename, textFolder.Children[1].Entry!.Resource.Filename);
        window.Close();
    }
}
