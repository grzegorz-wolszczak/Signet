using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// Rendering of the "Find Usages" panel: the usages tree (a TreeDataGrid with Usage, Line, Col., Kind, Context), its
/// keyboard handling and the "Group By" menu of the left toolbar.
/// </summary>
public sealed class FindUsagesViewTests
{
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static TreeDataGrid Tree(Window window) =>
        window.GetVisualDescendants().OfType<TreeDataGrid>().Single(t => t.Name == "UsageList");

    // The realized, visible rows of the tree, in display order (recycled rows are hidden).
    private static FindUsagesNode[] RealizedRows(Window window) =>
        Tree(window).GetVisualDescendants().OfType<TreeDataGridRow>()
            .Where(r => r.IsVisible && r.RowIndex >= 0)
            .OrderBy(r => r.RowIndex)
            .Select(r => (FindUsagesNode)r.Model!)
            .ToArray();

    // The row index of a node (by its index path from the root).
    private static int RowIndexOf(TreeDataGrid tree, FindUsagesNode node)
    {
        System.Collections.Generic.List<int> path = new();
        for (FindUsagesNode current = node; current.Parent is { } parent; current = parent)
        {
            path.Insert(0, parent.Children.ToList().IndexOf(current));
        }

        path.Insert(0, 0);
        return tree.Rows!.ModelIndexToRowIndex(new IndexPath(path));
    }

    // The first cell of a node's row (in a TreeDataGrid the cells take the keyboard focus).
    private static Control FirstCell(TreeDataGrid tree, FindUsagesNode node) => tree.TryGetCell(0, RowIndexOf(tree, node))!;

    // Like a click on the row: selects it and focuses its first cell.
    private static void SelectAndFocus(TreeDataGrid tree, FindUsagesNode node)
    {
        tree.RowSelection!.SelectedIndex = tree.Rows!.RowIndexToModelIndex(RowIndexOf(tree, node));
        FirstCell(tree, node).Focus();
    }

    private static (Window Window, FindUsagesViewModel Vm, TreeDataGrid Tree) ShowPanel(TempDir temp, bool groupByFile, params ClassUsage[] usages)
    {
        FindUsagesViewModel vm = new(new SettingsStore(temp.Combine("settings.xml"))) { GroupByFile = groupByFile };
        vm.Load("note", usages);
        Window window = new() { Width = 1400, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        return (window, vm, Tree(window));
    }

    [AvaloniaFact]
    public void The_columns_show_the_line_column_kind_and_context_of_every_usage()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(
            temp,
            groupByFile: false,
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>"),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector, ".note { color: red }"));

        // Assert
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("FindUsages_ColumnUsage"), Strings.Get("ValidationResultsView_Line"), Strings.Get("FindUsages_ColumnColumn"),
            Strings.Get("FindUsages_ColumnKind"), Strings.Get("FindUsages_ColumnContext"));
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
            .Should().Contain("3").And.Contain("14")
            .And.Contain(Strings.Get("FindUsages_KindAttribute")).And.Contain(Strings.Get("FindUsages_KindSelector"));
        tree.GetVisualDescendants().OfType<UsageContextText>().Select(ContextOf)
            .Should().Contain("<p class=\"note\">x</p>").And.Contain(".note { color: red }");
        window.Close();
    }

    // The text of a "Context" cell: its runs put together.
    private static string ContextOf(UsageContextText cell) => string.Concat(cell.Inlines!.OfType<Run>().Select(r => r.Text));

    private static (Window Window, FindUsagesViewModel Vm, TreeDataGrid Tree) ShowPanel(
        SettingsStore settings, double width, params ClassUsage[] usages)
    {
        FindUsagesViewModel vm = new(settings);
        vm.Load("note", usages);
        Window window = new() { Width = width, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        return (window, vm, Tree(window));
    }

    [AvaloniaFact]
    public void The_Context_cell_colors_the_syntax_and_marks_the_searched_class_like_a_Find_match()
    {
        // Arrange
        using TempDir temp = new();
        const string context = "<span class=\"note\">x</span>";
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, context, 0, 13, 4);

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(temp, groupByFile: false, usage);

        UsageContextText cell = tree.GetVisualDescendants().OfType<UsageContextText>().Single(c => c.Node?.Usage is not null);

        // Assert
        ContextOf(cell).Should().Be(context);
        Run[] runs = cell.Inlines!.OfType<Run>().ToArray();
        Run match = runs.Single(r => r.Text == "note");
        match.Background.Should().BeAssignableTo<ISolidColorBrush>().Which.Color
            .Should().Be(AppTheme.Current.Background(ThemeKeys.SearchMatch)!.Value);
        runs.Where(r => !ReferenceEquals(r, match)).Should().OnlyContain(r => r.Background == null, "only the match is marked");
        runs.Select(r => r.Foreground).Where(f => f is not null).Distinct().Should().HaveCountGreaterThan(1, "tags and attributes have colors");
        window.Close();
    }

    [AvaloniaFact]
    public void The_fonts_of_the_headers_the_context_and_the_other_cells_follow_the_settings()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"))
        {
            FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 21, string.Empty, 17, string.Empty, 15),
        };
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>", 0, 10, 4);

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(settings, 1400, usage);

        void Check(int header, int context, int cells)
        {
            tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Should().OnlyContain(h => h.FontSize == header);
            tree.GetVisualDescendants().OfType<UsageContextText>().Should().OnlyContain(c => c.FontSize == context);
            tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Should().OnlyContain(c => c.FontSize == cells);
            window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "HeadingText").FontSize.Should().Be(cells);
        }

        // Assert
        Check(21, 17, 15);

        // Act
        settings.FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, string.Empty, 19, string.Empty, 0);
        Settle(window);

        TreeDataGridColumnHeader anyHeader = tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().First();

        // Assert
        anyHeader.FontSize.Should().NotBe(21, "a size left to the interface follows it again");
        tree.GetVisualDescendants().OfType<UsageContextText>().Should().OnlyContain(c => c.FontSize == 19);
        window.Close();
    }

    [AvaloniaFact]
    public void The_typeface_of_the_Context_column_reaches_its_text_when_the_panel_opens_and_when_it_changes()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"))
        {
            FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, "Consolas", 0, string.Empty, 0),
        };
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>", 0, 10, 4);

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(settings, 1400, usage);
        UsageContextText Cell() => tree.GetVisualDescendants().OfType<UsageContextText>().Single(c => c.Node?.Usage is not null);

        // Assert
        Cell().FontFamily.Name.Should().Be("Consolas");
        Cell().Inlines!.OfType<Run>().Should().OnlyContain(r => r.FontFamily.Name == "Consolas", "the runs follow the cell");

        // Act
        settings.FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, "Courier New", 0, string.Empty, 0);
        Settle(window);

        // Assert
        Cell().FontFamily.Name.Should().Be("Courier New");
        window.Close();
    }

    [AvaloniaFact]
    public void The_row_spacing_leaves_a_gap_between_the_rows_that_the_cell_backgrounds_do_not_cover()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            settings, 1400,
            new ClassUsage("a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("b.xhtml", 10, 4, 14, ClassUsageKind.ClassAttribute));
        FindUsagesNode root = vm.Roots.Single();
        double Gap()
        {
            Control first = tree.TryGetRow(RowIndexOf(tree, root.Children[0]))!;
            Control second = tree.TryGetRow(RowIndexOf(tree, root.Children[1]))!;
            return second.TranslatePoint(new Point(0, 0), tree)!.Value.Y - first.TranslatePoint(new Point(0, first.Bounds.Height), tree)!.Value.Y;
        }

        // Assert
        Gap().Should().BeApproximately(0, 0.5, "the rows touch by default");

        // Act
        settings.FindUsagesRowSpacing = 6;
        vm.ReloadSettings();
        Settle(window);

        // Assert
        vm.RowSpacing.Should().Be(6);
        Gap().Should().BeApproximately(6, 0.5);

        // Act
        settings.FindUsagesRowSpacing = 0;
        vm.ReloadSettings();
        Settle(window);

        // Assert
        Gap().Should().BeApproximately(0, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void The_weights_of_the_headers_the_context_and_the_other_cells_follow_the_settings()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"))
        {
            FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, string.Empty, 0, string.Empty, 0, 700, 300, 200),
        };
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>", 0, 10, 4);

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(settings, 1400, usage);

        // Assert
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Should().OnlyContain(h => h.FontWeight == FontWeight.Bold);
        tree.GetVisualDescendants().OfType<UsageContextText>().Should().OnlyContain(c => c.FontWeight == FontWeight.Light);
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Should().OnlyContain(c => c.FontWeight == FontWeight.ExtraLight);
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "HeadingText").FontWeight.Should().Be(FontWeight.ExtraLight);

        // Act
        settings.FindUsagesAppearance = FindUsagesAppearance.Default;
        Settle(window);

        // Assert
        string weights = string.Join(", ", tree.GetVisualDescendants().OfType<UsageContextText>().Select(c => c.FontWeight)
            .Concat(tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(c => c.FontWeight))
            .Concat(tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.FontWeight)));
        weights.Should().NotContain("Light").And.NotContain("Bold").And.NotContain("ExtraLight");
        tree.GetVisualDescendants().OfType<UsageContextText>().Should().OnlyContain(c => c.FontWeight == FontWeight.Normal);
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "HeadingText").FontWeight
            .Should().Be(FontWeight.SemiBold, "the heading is semi-bold unless the cells have a weight of their own");
        window.Close();
    }

    [AvaloniaFact]
    public void A_bigger_font_of_the_cells_makes_the_fitted_Usage_column_wider()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(
            settings, 1400, new ClassUsage("OEBPS/Text/chapter.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute));
        double before = tree.Columns![0].ActualWidth;

        settings.FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, string.Empty, 0, string.Empty, 30);
        Settle(window);

        // Assert
        tree.Columns[0].ActualWidth.Should().BeGreaterThan(before);
        window.Close();
    }

    [AvaloniaFact]
    public void Every_column_takes_the_background_its_theme_key_sets_and_follows_a_change_of_the_theme()
    {
        // Arrange
        using TempDir temp = new();
        Application app = Application.Current!;
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>", 0, 10, 4);

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(new SettingsStore(temp.Combine("settings.xml")), 1400, usage);
        FindUsagesNode node = vm.Roots.Single().Children[0];
        Control Cell(int column) => tree.TryGetCell(column, RowIndexOf(tree, node))!;
        Color? ColorOf(Control cell) => (((TemplatedControl)cell).Background as ISolidColorBrush)?.Color;
        Theme theme = new Theme(Ulid.NewUlid().ToString(), "Columns")
            .With(ThemeKeys.UiFindUsagesUsageBackground.Id, ThemeEntry.Of(ThemeColor.Parse("#112233")))
            .With(ThemeKeys.UiFindUsagesLineBackground.Id, ThemeEntry.Of(ThemeColor.Parse("#223344")))
            .With(ThemeKeys.UiFindUsagesColumnBackground.Id, ThemeEntry.Of(ThemeColor.Parse("#334455")))
            .With(ThemeKeys.UiFindUsagesKindBackground.Id, ThemeEntry.Of(ThemeColor.Parse("#445566")))
            .With(ThemeKeys.UiFindUsagesContextBackground.Id, ThemeEntry.Of(ThemeColor.Parse("#556677")));
        try
        {
            // Assert
            ColorOf(Cell(1)).Should().NotBe(Color.Parse("#223344"), "no color is set by default");

            // Act
            AppTheme.Apply(app, ResolvedTheme.Of(ThemeDocumentKind.Theme, theme, new ThemeResolver(_ => null)));
            Settle(window);

            string[] expected = { "#112233", "#223344", "#334455", "#445566", "#556677" };
            for (int column = 0; column < expected.Length; column++)
            {
                // Assert
                ColorOf(Cell(column)).Should().Be(Color.Parse(expected[column]), "column {0}", column);
            }

            // Act
            tree.RowSelection!.SelectedIndex = tree.Rows!.RowIndexToModelIndex(RowIndexOf(tree, node));
            Settle(window);

            // Assert
            ColorOf(Cell(1)).Should().NotBe(Color.Parse("#223344"), "the selected row keeps the color of the selection");

            // Act
            tree.RowSelection.Clear();
            AppTheme.Apply(app, ResolvedTheme.Defaults);
            Settle(window);

            for (int column = 0; column < expected.Length; column++)
            {
                // Assert
                ColorOf(Cell(column)).Should().NotBe(Color.Parse(expected[column]), "a color the theme no longer sets goes away (column {0})", column);
            }
        }
        finally
        {
            AppTheme.Apply(app, ResolvedTheme.Defaults);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_Usage_column_is_as_wide_as_its_widest_entry_plus_a_margin()
    {
        // Arrange
        using TempDir temp = new();
        ClassUsage shortOne = new("a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute);
        ClassUsage longOne = new("OEBPS/Text/a_rather_long_chapter_file_name.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute);

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(new SettingsStore(temp.Combine("settings.xml")), 900, shortOne, longOne);

        double widest = Math.Max(Label(tree, vm.Roots.Single().Children[1]), Label(tree, vm.Roots.Single()) - 20);
        double width = tree.Columns![0].ActualWidth;

        // Assert
        tree.Columns[0].Width.IsAbsolute.Should().BeTrue("the column is fitted, not a share of the grid");
        width.Should().BeInRange(widest + 20 + 20 + 10, widest + 20 + 20 + 10 + 2, "indent of a usage, the toggle and the margin");

        // Act
        vm.Load("note", new[] { shortOne });
        Settle(window);

        // Assert
        tree.Columns[0].ActualWidth.Should().BeLessThan(width, "a new search fits the column to its own entries");
        window.Close();
    }

    // The width of the label of a node in the font of the grid.
    private static double Label(TreeDataGrid tree, FindUsagesNode node) =>
        new FormattedText(
            node.Text + node.CountSuffix, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(tree.FontFamily, tree.FontStyle, tree.FontWeight), tree.FontSize, null).Width;

    [AvaloniaFact]
    public void Without_Fit_the_Usage_column_keeps_the_width_stored_and_remembers_the_one_the_user_gives_it()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml")) { FindUsagesFitUsageColumn = false, FindUsagesUsageColumnWidth = 333 };

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            settings, 900, new ClassUsage("a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute));

        // Assert
        tree.Columns![0].ActualWidth.Should().Be(333);

        // Act
        ((IColumns)tree.Columns).SetColumnWidth(0, new GridLength(280, GridUnitType.Pixel)); // what dragging the header does
        Point point = tree.TranslatePoint(new Point(tree.Bounds.Width / 2, tree.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);

        // Assert
        settings.FindUsagesUsageColumnWidth.Should().Be(280);

        // Act
        vm.Load("other", new[] { new ClassUsage("b.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute) });
        Settle(window);

        // Assert
        tree.Columns[0].ActualWidth.Should().Be(280, "another search does not change a width that is not fitted");
        window.Close();
    }

    [AvaloniaFact]
    public void Turning_Fit_off_keeps_the_width_the_column_has_then()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            settings, 900, new ClassUsage("OEBPS/Text/chapter.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute));
        double fitted = tree.Columns![0].ActualWidth;

        settings.FindUsagesFitUsageColumn = false;
        vm.ReloadSettings();
        Settle(window);

        // Assert
        vm.FitUsageColumn.Should().BeFalse();
        settings.FindUsagesUsageColumnWidth.Should().Be((int)Math.Round(fitted));
        window.Close();
    }

    [AvaloniaFact]
    public void Wrap_Rows_wraps_a_long_context_and_makes_its_row_taller()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        string context = "<p class=\"note\">" + string.Join(' ', Enumerable.Repeat("a long long text", 40)) + "</p>";

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            settings, 900, new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, context, 0, 12, 4));
        FindUsagesNode usage = vm.Roots.Single().Children[0];
        double Height() => tree.TryGetRow(RowIndexOf(tree, usage))!.Bounds.Height;
        double single = Height();

        ToggleButton wrap = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "WrapRowsButton");
        Point center = wrap.TranslatePoint(new Point(wrap.Bounds.Width / 2, wrap.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        // Assert
        vm.WrapRows.Should().BeTrue();
        settings.FindUsagesWrapRows.Should().BeTrue("the choice is remembered");
        Height().Should().BeGreaterThan(single * 2, "the long context is on several lines");

        // Act
        vm.WrapRows = false;
        Settle(window);

        // Assert
        Height().Should().Be(single);
        window.Close();
    }

    // Clicks a column header (sorts by that column; a second click reverses the order).
    private static void ClickHeader(Window window, TreeDataGrid tree, string headerKey)
    {
        TreeDataGridColumnHeader header = tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>()
            .Single(h => Equals(h.Header, Strings.Get(headerKey)));
        Point center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Usage_column_sorts_by_file_then_by_line_and_column_as_numbers(bool groupByFile)
    {
        // Arrange
        using TempDir temp = new();
        ClassUsage line100 = new("OEBPS/Text/a.xhtml", 900, 100, 3, ClassUsageKind.ClassAttribute);
        ClassUsage line20Col9 = new("OEBPS/Text/a.xhtml", 300, 20, 9, ClassUsageKind.ClassAttribute);
        ClassUsage line20Col2 = new("OEBPS/Text/a.xhtml", 290, 20, 2, ClassUsageKind.ClassAttribute);
        ClassUsage css = new("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector);

        // Act
        (Window window, _, TreeDataGrid tree) = ShowPanel(temp, groupByFile, line100, line20Col9, css, line20Col2);

        ClickHeader(window, tree, "FindUsages_ColumnUsage");

        ClassUsage?[] ascending = RealizedRows(window).Select(n => n.Usage).Where(u => u is not null).ToArray();

        // Assert
        ascending.Should().Equal(css, line20Col2, line20Col9, line100);
        if (groupByFile)
        {
            RealizedRows(window).Where(n => n.Usage is null).Select(n => n.Text)
                .Should().Equal(Strings.Get("FindUsages_Found"), "OEBPS/Styles/s.css", "OEBPS/Text/a.xhtml");
        }

        // Act
        ClickHeader(window, tree, "FindUsages_ColumnUsage");

        // Assert
        RealizedRows(window).Select(n => n.Usage).Where(u => u is not null).Should().Equal(ascending.Reverse());
        window.Close();
    }

    [AvaloniaFact]
    public void The_tree_shows_the_usages_and_Group_By_File_switches_to_one_node_per_file()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        FindUsagesViewModel vm = new(settings);
        vm.Load("note", new[]
        {
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Text/a.xhtml", 40, 5, 9, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector),
        });

        // Act
        Window window = new() { Width = 900, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);

        // Assert
        RealizedRows(window).Select(n => n.Text)
            .Should().Equal(Strings.Get("FindUsages_Found"), "OEBPS/Text/a.xhtml:3:14", "OEBPS/Text/a.xhtml:5:9", "OEBPS/Styles/s.css:1:2");

        Button groupBy = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupByButton");

        // Act
        groupBy.Flyout!.ShowAt(groupBy);
        Settle(window);
        MenuItem file = window.GetVisualDescendants().OfType<MenuItem>().SingleOrDefault(m => m.Name == "GroupByFileItem")
            ?? TopLevel.GetTopLevel(groupBy)!.GetVisualDescendants().OfType<MenuItem>().Single(m => m.Name == "GroupByFileItem");

        // Assert
        file.IsChecked.Should().BeFalse();
        Point center = file.TranslatePoint(new Point(file.Bounds.Width / 2, file.Bounds.Height / 2), window)!.Value;

        // Act
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        // Assert
        vm.GroupByFile.Should().BeTrue();
        settings.FindUsagesGroupByFile.Should().BeTrue();
        RealizedRows(window).Select(n => n.Text)
            .Should().Equal(
                Strings.Get("FindUsages_Found"),
                "OEBPS/Text/a.xhtml", Strings.Format("FindUsages_LineColumn", 3, 14), Strings.Format("FindUsages_LineColumn", 5, 9),
                "OEBPS/Styles/s.css", Strings.Format("FindUsages_LineColumn", 1, 2));
        window.Close();
    }

    [AvaloniaFact]
    public void Collapse_All_button_and_Ctrl_NumPad_plus_collapse_and_expand_the_tree()
    {
        // Arrange
        using TempDir temp = new();
        FindUsagesViewModel vm = new(new SettingsStore(temp.Combine("settings.xml"))) { GroupByFile = true };
        vm.Load("note", new[]
        {
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector),
        });

        // Act
        Window window = new() { Width = 900, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        string[] VisibleRows() => RealizedRows(window).Select(n => n.Text).ToArray();

        // Assert
        VisibleRows().Should().HaveCount(5);

        Button collapse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CollapseAllButton");
        Point center = collapse.TranslatePoint(new Point(collapse.Bounds.Width / 2, collapse.Bounds.Height / 2), window)!.Value;

        // Act
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        // Assert
        VisibleRows().Should().Equal(Strings.Get("FindUsages_Found"));

        // Act
        SelectAndFocus(Tree(window), vm.Roots.Single());
        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Settle(window);

        // Assert
        VisibleRows().Should().HaveCount(5, "Ctrl+NumPad + expands everything again");
        window.Close();
    }
    [AvaloniaFact]
    public void A_huge_result_list_creates_rows_only_for_the_visible_part()
    {
        // Arrange
        using TempDir temp = new();
        const int count = 20_000;
        ClassUsage[] usages = Enumerable.Range(0, count)
            .Select(i => new ClassUsage($"OEBPS/Text/ch{i / 400:D3}.xhtml", i * 10, i % 400 + 1, 5, ClassUsageKind.ClassAttribute))
            .ToArray();

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(temp, groupByFile: false, usages);

        // Assert
        tree.Rows!.Count.Should().Be(count + 1);
        RealizedRows(window).Should().HaveCountLessThan(100, "a 400 px panel shows a few dozen rows at most")
            .And.HaveElementAt(0, vm.Roots.Single());

        // Act
        tree.RowsPresenter!.BringIntoView(count);
        Settle(window);

        // Assert
        RealizedRows(window).Should().HaveCountLessThan(100).And.Contain(vm.Roots.Single().Children[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void Left_and_Right_collapse_and_expand_the_selected_node_and_move_to_the_parent_and_the_first_child()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            temp,
            groupByFile: true,
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Text/a.xhtml", 40, 5, 9, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector));
        FindUsagesNode root = vm.Roots.Single();
        FindUsagesNode file = root.Children[0];
        SelectAndFocus(tree, file);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Settle(window);

        // Assert
        file.IsExpanded.Should().BeFalse();
        RealizedRows(window).Should().Equal(root, file, root.Children[1], root.Children[1].Children[0]);

        // Act
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Settle(window);

        // Assert
        file.IsExpanded.Should().BeTrue();
        RealizedRows(window).Should().HaveCount(6);

        // Act
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        // Assert
        tree.RowSelection!.SelectedItem.Should().BeSameAs(file.Children[0], "Right on an expanded node goes to its first child");
        FirstCell(tree, file.Children[0]).IsFocused.Should().BeTrue("the keyboard focus follows the selection");

        // Act
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);

        // Assert
        tree.RowSelection.SelectedItem.Should().BeSameAs(file, "Left on a usage goes to its file");
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_on_a_usage_opens_it()
    {
        // Arrange
        using TempDir temp = new();
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute);

        // Act
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(temp, groupByFile: false, usage);
        ClassUsage? activated = null;
        vm.UsageActivated += (_, u) => activated = u;
        SelectAndFocus(tree, vm.Roots.Single().Children[0]);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        // Assert
        activated.Should().Be(usage);
        window.Close();
    }
}
