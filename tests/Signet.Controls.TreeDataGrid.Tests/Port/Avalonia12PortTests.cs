using System;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests.Port
{
    /// <summary>
    /// Signet's own tests of the fork: the places changed by the Avalonia 12 port (see UPSTREAM.md) and the shipped
    /// Fluent theme, which the upstream tests never load (they use code templates).
    /// </summary>
    public class Avalonia12PortTests : IDisposable
    {
        // Fluent goes into the application styles, as in Signet (App.axaml): the fork's Fluent.axaml uses
        // {StaticResource SystemListLowColor} etc., which is resolved when the theme is loaded and so must be reachable
        // through Application.Current. Removed again after the test - the upstream tests run without a theme.
        private readonly FluentTheme _fluent = new();

        public Avalonia12PortTests()
        {
            Application.Current!.Styles.Add(_fluent);
        }

        public void Dispose()
        {
            Application.Current!.Styles.Remove(_fluent);
        }

        private const string ThemeUri = "avares://Signet.Controls.TreeDataGrid/Themes/Fluent.axaml";

        private class Model : NotifyingBase
        {
            private string? _title;

            public int Id { get; set; }

            public string? Title
            {
                get => _title;
                set => RaiseAndSetIfChanged(ref _title, value);
            }

            public AvaloniaList<Model> Children { get; } = new();
        }

        // A window with the fork's theme, so the control uses its real templates. The styles go in
        // before the content: TestWindow is shown (and templates applied) in its constructor.
        private static TestWindow ThemedWindow(Control content, Size size)
        {
            var window = new TestWindow(size);
            window.Styles.Add(new StyleInclude(new Uri("avares://Signet.Controls.TreeDataGrid.Tests/"))
            {
                Source = new Uri(ThemeUri),
            });
            window.Content = content;
            Settle(window);
            return window;
        }

        private static void Settle(TestWindow window)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static AvaloniaList<Model> FlatItems(int count) =>
            new(Enumerable.Range(0, count).Select(i => new Model { Id = i, Title = "Item " + i }));

        private static Point CenterOf(Visual visual, Visual relativeTo) =>
            visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), relativeTo)!.Value;

        [AvaloniaFact(Timeout = 10000)]
        public void Fluent_theme_renders_a_hierarchical_grid_and_realizes_only_the_visible_rows()
        {
            var root = new Model { Id = -1, Title = "Root" };
            root.Children.AddRange(FlatItems(10_000));
            var source = new HierarchicalTreeDataGridSource<Model>(new[] { root })
            {
                Columns =
                {
                    new HierarchicalExpanderColumn<Model>(new TextColumn<Model, string?>("Title", x => x.Title), x => x.Children),
                    new TextColumn<Model, int>("ID", x => x.Id),
                },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(400, 300));

            source.Expand(new IndexPath(0));
            Settle(window);

            source.Rows.Count.Should().Be(10_001);
            target.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header)
                .Should().Equal("Title", "ID");
            target.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Should().NotBeEmpty();
            target.RowsPresenter!.RealizedElements.OfType<TreeDataGridRow>().Where(r => r.IsVisible)
                .Should().HaveCountGreaterThan(5).And.HaveCountLessThan(100, "a 300 px viewport shows a few dozen rows");
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Dropping_a_DragInfo_from_a_DataTransfer_moves_the_row()
        {
            var items = FlatItems(10);
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title) },
            };
            var target = new TreeDataGrid { Source = source, AutoDragDropRows = true };
            var window = ThemedWindow(target, new Size(300, 500));
            // Over the cell (upper half = "before"). The theme gives cells a transparent background, but the single
            // Auto-width column is narrower than the window and the area right of it is not part of any row.
            var targetCell = target.TryGetCell(0, 5)!;
            var point = targetCell.TranslatePoint(new Point(10, targetCell.Bounds.Height * 0.25), window)!.Value;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragInfo.DataFormat, new DragInfo(source, new[] { new IndexPath(0) })));

            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Move, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Move, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Move, RawInputModifiers.None);
            Settle(window);

            items.Select(x => x.Id).Should().Equal(1, 2, 3, 4, 0, 5, 6, 7, 8, 9);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void DragInfo_round_trips_through_a_DataTransfer_in_process()
        {
            var source = new FlatTreeDataGridSource<Model>(FlatItems(3));
            var info = new DragInfo(source, new[] { new IndexPath(1) });
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragInfo.DataFormat, info));

            data.TryGetValue(DragInfo.DataFormat).Should().BeSameAs(info);
            data.Formats.Should().Contain(DragInfo.DataFormat);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Double_tapping_a_double_tap_editable_cell_begins_editing()
        {
            var items = FlatItems(5);
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns =
                {
                    new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v,
                        options: new TextColumnOptions<Model> { BeginEditGestures = BeginEditGestures.DoubleTap }),
                },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            var cell = target.TryGetCell(0, 2).Should().BeOfType<TreeDataGridTextCell>().Subject;
            var point = CenterOf(cell, window);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Settle(window);

            cell.IsEditing.Should().BeFalse("a single tap is not the configured gesture");

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Settle(window);

            cell.IsEditing.Should().BeTrue("the second click of a double-click raises DoubleTapped (OnDoubleTapped override)");
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Moving_the_focus_out_of_an_editing_cell_ends_the_edit_and_commits_the_value()
        {
            var items = FlatItems(5);
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v) },
            };
            var target = new TreeDataGrid { Source = source, Height = 200 };
            var other = new Button { Content = "Other" };
            var window = ThemedWindow(new StackPanel { Children = { target, other } }, new Size(300, 300));
            var cell = target.TryGetCell(0, 1).Should().BeOfType<TreeDataGridTextCell>().Subject;
            cell.BeginEdit();
            Settle(window);
            var editor = cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle().Subject;
            editor.Focus();
            editor.Text = "Changed";

            other.Focus();
            Settle(window);

            cell.IsEditing.Should().BeFalse();
            items[1].Title.Should().Be("Changed");
        }

        [AvaloniaFact(Timeout = 10000)]
        public void BeginEdit_is_public_so_a_command_can_start_editing_a_cell()
        {
            var items = FlatItems(3);
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            var cell = target.TryGetCell(0, 1).Should().BeOfType<TreeDataGridTextCell>().Subject;

            cell.BeginEdit();
            Settle(window);

            cell.IsEditing.Should().BeTrue();
            cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle();
        }

        [AvaloniaFact(Timeout = 10000)]
        public void The_expander_of_a_row_that_cannot_expand_goes_back_to_collapsed()
        {
            // A "group" with a chevron (hasChildren: true) but no children yet.
            var empty = new Model { Id = 0, Title = "Empty group" };
            var source = new HierarchicalTreeDataGridSource<Model>(new[] { empty })
            {
                Columns =
                {
                    new HierarchicalExpanderColumn<Model>(
                        new TextColumn<Model, string?>("Title", x => x.Title), x => x.Children, x => true),
                },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 200));
            var cell = target.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Single();
            var toggle = cell.GetVisualDescendants().OfType<ToggleButton>().Single();
            cell.ShowExpander.Should().BeTrue();

            var point = CenterOf(toggle, window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Settle(window);

            cell.IsExpanded.Should().BeFalse("a row without children does not expand");
            toggle.IsChecked.Should().BeFalse("the chevron must not stay in the expanded state");
        }

        [AvaloniaTheory(Timeout = 10000)]
        [InlineData(1.0, 1.0, true)]
        [InlineData(0.1 + 0.2, 0.3, true)]
        [InlineData(1e6, 1e6 + 1e-10, true)]
        [InlineData(1e6, 1e6 + 1e-9, false)]
        [InlineData(1.0, 1.0001, false)]
        [InlineData(double.PositiveInfinity, double.PositiveInfinity, true)]
        public void MathUtilities_AreClose_uses_a_relative_tolerance(double a, double b, bool expected)
        {
            MathUtilities.AreClose(a, b).Should().Be(expected);
            MathUtilities.AreClose(b, a).Should().Be(expected);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void MathUtilities_IsZero_and_GreaterThan_ignore_rounding_noise()
        {
            MathUtilities.IsZero(0).Should().BeTrue();
            MathUtilities.IsZero(1e-16).Should().BeTrue();
            MathUtilities.IsZero(1e-10).Should().BeFalse();
            MathUtilities.GreaterThan(2, 1).Should().BeTrue();
            MathUtilities.GreaterThan(1 + 1e-17, 1).Should().BeFalse();
            MathUtilities.GreaterThan(1, 2).Should().BeFalse();
        }
    }
}
