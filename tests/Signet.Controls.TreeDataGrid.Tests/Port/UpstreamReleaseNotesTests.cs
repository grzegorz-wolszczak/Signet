using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// The bugs that later (commercial) versions of the control list as fixed in their public release notes, checked
    /// against this fork: those it had are fixed here (see "Fixes" in UPSTREAM.md), the others are regression tests.
    /// Each test names the release note it comes from.
    /// </summary>
    public class UpstreamReleaseNotesTests : IDisposable
    {
        private readonly FluentTheme _fluent = new();

        public UpstreamReleaseNotesTests()
        {
            Application.Current!.Styles.Add(_fluent);
        }

        public void Dispose()
        {
            Application.Current!.Styles.Remove(_fluent);
        }

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

        // A collection that replaces a range of items with one Replace notification (ObservableCollection replaces
        // one item at a time).
        private sealed class RangeReplaceList : Collection<Model>, INotifyCollectionChanged
        {
            public RangeReplaceList(IEnumerable<Model> items)
                : base(items.ToList())
            {
            }

            public event NotifyCollectionChangedEventHandler? CollectionChanged;

            public void ReplaceRange(int index, IList<Model> newItems)
            {
                var oldItems = new List<Model>();
                for (var i = 0; i < newItems.Count; i++)
                {
                    oldItems.Add(Items[index + i]);
                    Items[index + i] = newItems[i];
                }

                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace, newItems.ToList(), oldItems, index));
            }
        }

        private static TestWindow ThemedWindow(Control content, Size size)
        {
            var window = new TestWindow(size);
            window.Styles.Add(new StyleInclude(new Uri("avares://Signet.Controls.TreeDataGrid.Tests/"))
            {
                Source = new Uri("avares://Signet.Controls.TreeDataGrid/Themes/Fluent.axaml"),
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

        private static List<Model> Items(int count) =>
            Enumerable.Range(0, count).Select(i => new Model { Id = i, Title = "Item " + i }).ToList();

        private static Point CenterOf(Visual visual, Visual relativeTo) =>
            visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), relativeTo)!.Value;

        private static void Click(TestWindow window, Point point)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Settle(window);
        }

        private static void Click(TestWindow window, Visual visual) => Click(window, CenterOf(visual, window));

        private static void Press(TestWindow window, PhysicalKey key)
        {
            window.KeyPressQwerty(key, RawInputModifiers.None);
            Settle(window);
        }

        // Two 400 px columns in a 300 px window: the rows are wider than the viewport.
        private static (FlatTreeDataGridSource<Model> Source, TreeDataGrid Target, TestWindow Window) WideGrid(int rows)
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(rows)))
            {
                Columns =
                {
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(400)),
                    new TextColumn<Model, int>("Id", x => x.Id, new GridLength(400)),
                },
            };
            var target = new TreeDataGrid { Source = source };
            return (source, target, ThemedWindow(target, new Size(300, 300)));
        }

        // Root > 3 children > 1 grandchild each, with an editable title.
        private static (HierarchicalTreeDataGridSource<Model> Source, TreeDataGrid Target, TestWindow Window, Model Root) Tree()
        {
            var root = new Model { Id = 0, Title = "Root" };
            for (var i = 0; i < 3; i++)
            {
                var child = new Model { Id = 10 + i, Title = "Child " + i };
                child.Children.Add(new Model { Id = 100 + i, Title = "Grandchild " + i });
                root.Children.Add(child);
            }

            var source = new HierarchicalTreeDataGridSource<Model>(new[] { root })
            {
                Columns =
                {
                    new HierarchicalExpanderColumn<Model>(
                        new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v), x => x.Children),
                },
            };
            var target = new TreeDataGrid { Source = source };
            return (source, target, ThemedWindow(target, new Size(300, 300)), root);
        }

        // Records whether KeyDown events of the key were handled by the time they reached the window.
        private static Func<bool?> HandledAtWindow(TestWindow window, Key key)
        {
            bool? handled = null;
            window.AddHandler(
                InputElement.KeyDownEvent,
                (_, e) =>
                {
                    if (e.Key == key)
                        handled = e.Handled;
                },
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            return () => handled;
        }

        // 11.3.2: "Fix PageUp jumping to start when columns wider than viewport". (Fixed here.)
        [AvaloniaTheory(Timeout = 10000)]
        [InlineData(200)]
        [InlineData(900)]
        public void PageUp_and_PageDown_move_by_a_page_also_when_the_columns_are_wider_than_the_viewport(double width)
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(300)))
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(width)) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            target.RowsPresenter!.BringIntoView(150);
            Settle(window);
            target.Scroll!.Offset = new Vector(0, target.Scroll.Offset.Y + 100);
            Settle(window);
            Click(window, target.TryGetCell(0, 150)!.TranslatePoint(new Point(20, 5), window)!.Value);
            source.RowSelection!.SelectedIndex.Should().Be(new IndexPath(150));

            Press(window, PhysicalKey.PageUp);
            Press(window, PhysicalKey.PageUp);

            var up = source.RowSelection.SelectedIndex[0];
            up.Should().BeInRange(120, 145, "two pages of about ten rows up, not the first row");

            Press(window, PhysicalKey.PageDown);
            Press(window, PhysicalKey.PageDown);

            source.RowSelection.SelectedIndex[0].Should().BeInRange(up + 10, up + 30);
        }

        // 12.3.1: "Fix rows showing stale data when a collection change replaces a range of items starting above the
        // first visible row". (Fixed here.)
        [AvaloniaFact(Timeout = 10000)]
        public void Replacing_a_range_that_starts_above_the_first_realized_row_updates_the_realized_rows()
        {
            var items = new RangeReplaceList(Items(300));
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            target.RowsPresenter!.BringIntoView(100);
            Settle(window);
            var first = target.RowsPresenter.RealizedElements.OfType<TreeDataGridRow>().Min(r => r.RowIndex);
            first.Should().BeGreaterThan(20);

            items.ReplaceRange(first - 10, Enumerable.Range(0, 20).Select(i => new Model { Title = "New" }).ToList());
            Settle(window);

            for (var row = first; row < first + 10; row++)
                ((TreeDataGridTextCell)target.TryGetCell(0, row)!).Value.Should().Be("New");
        }

        // 12.0.3: "Arrow-key navigation to parent/child rows now properly marked as handled". (Fixed here.)
        [AvaloniaFact(Timeout = 10000)]
        public void Left_on_a_collapsed_child_selects_and_focuses_the_parent_and_is_handled()
        {
            var (source, target, window, root) = Tree();
            source.Expand(new IndexPath(0));
            Settle(window);
            Click(window, target.TryGetCell(0, 1)!);
            var handled = HandledAtWindow(window, Key.Left);

            Press(window, PhysicalKey.ArrowLeft);

            source.RowSelection!.SelectedItem.Should().BeSameAs(root);
            handled().Should().BeTrue();
            (window.FocusManager!.GetFocusedElement() as Visual)!.FindAncestorOfType<TreeDataGridRow>(includeSelf: true)!
                .RowIndex.Should().Be(0);
        }

        // 12.0.3, as above: Right on an expanded row. (Fixed here; upstream also left the focus on the parent.)
        [AvaloniaFact(Timeout = 10000)]
        public void Right_on_an_expanded_row_selects_and_focuses_its_first_child_and_is_handled()
        {
            var (source, target, window, root) = Tree();
            source.Expand(new IndexPath(0));
            Settle(window);
            Click(window, target.TryGetCell(0, 0)!);
            var handled = HandledAtWindow(window, Key.Right);

            Press(window, PhysicalKey.ArrowRight);

            source.RowSelection!.SelectedItem.Should().BeSameAs(root.Children[0]);
            handled().Should().BeTrue();
            (window.FocusManager!.GetFocusedElement() as Visual)!.FindAncestorOfType<TreeDataGridRow>(includeSelf: true)!
                .RowIndex.Should().Be(1);
        }

        // 12.1.0: "Text cells no longer display stale edited text after cancellation". (Fixed here.)
        [AvaloniaFact(Timeout = 10000)]
        public void Cancelling_an_edit_shows_the_models_text_again()
        {
            var items = new AvaloniaList<Model>(Items(5));
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            var cell = (TreeDataGridTextCell)target.TryGetCell(0, 1)!;
            Click(window, cell);
            cell.BeginEdit();
            Settle(window);
            var editor = cell.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Focus();
            editor.Text = "Changed";

            Press(window, PhysicalKey.Escape);

            cell.IsEditing.Should().BeFalse();
            items[1].Title.Should().Be("Item 1");
            cell.Value.Should().Be("Item 1");
            cell.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Item 1");

            cell.BeginEdit();
            Settle(window);
            cell.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Be("Item 1", "editing starts from the model");
        }

        // 12.0.3: "Stale selection after node replacement resolved". (Fixed here.)
        [AvaloniaFact(Timeout = 10000)]
        public void Replacing_a_node_deselects_the_selected_descendants_of_the_old_node()
        {
            var (source, _, window, root) = Tree();
            source.Expand(new IndexPath(0));
            source.Expand(new IndexPath(0, 0));
            Settle(window);
            var oldGrandchild = root.Children[0].Children[0];
            source.RowSelection!.Select(new IndexPath(0, 0, 0));
            source.RowSelection.SelectedItem.Should().BeSameAs(oldGrandchild);
            var deselected = new List<object?>();
            source.RowSelection.SelectionChanged += (_, e) => deselected.AddRange(e.DeselectedItems);

            root.Children[0] = new Model { Id = 99, Title = "Replacement" };
            Settle(window);

            source.RowSelection.SelectedItems.Should().BeEmpty();
            source.RowSelection.SelectedItem.Should().BeNull();
            deselected.Should().Contain(oldGrandchild);
        }

        // 12.0.3 (regression test): replacing the selected node itself.
        [AvaloniaFact(Timeout = 10000)]
        public void Replacing_the_selected_node_deselects_it()
        {
            var (source, _, window, root) = Tree();
            source.Expand(new IndexPath(0));
            Settle(window);
            var old = root.Children[1];
            source.RowSelection!.Select(new IndexPath(0, 1));

            root.Children[1] = new Model { Id = 99, Title = "Replacement" };
            Settle(window);

            source.RowSelection.SelectedItems.Should().NotContain(old);
        }

        // 12.2.0: "Unwanted horizontal scroll in row selection mode eliminated". (Fixed here.)
        [AvaloniaFact(Timeout = 10000)]
        public void Arrow_keys_keep_the_horizontal_scroll_offset_and_still_scroll_vertically()
        {
            var (source, target, window) = WideGrid(50);
            Click(window, target.TryGetCell(0, 2)!.TranslatePoint(new Point(20, 5), window)!.Value);
            var scroll = (ScrollViewer)target.Scroll!;
            scroll.Offset = new Vector(300, 0);
            Settle(window);

            Press(window, PhysicalKey.ArrowDown);

            source.RowSelection!.SelectedIndex.Should().Be(new IndexPath(3));
            scroll.Offset.X.Should().Be(300);

            for (var i = 0; i < 20; i++)
                Press(window, PhysicalKey.ArrowDown);

            source.RowSelection.SelectedIndex.Should().Be(new IndexPath(23));
            scroll.Offset.X.Should().Be(300);
            scroll.Offset.Y.Should().BeGreaterThan(0, "the selected row is brought into view vertically");
            var row = target.TryGetRow(23)!;
            var rowTop = row.TranslatePoint(default, scroll)!.Value.Y;
            rowTop.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(scroll.Viewport.Height - row.Bounds.Height + 1);
        }

        // 12.2.0, as above: clicking a cell of a horizontally scrolled grid does not scroll either.
        [AvaloniaFact(Timeout = 10000)]
        public void Clicking_a_partly_visible_cell_keeps_the_horizontal_scroll_offset()
        {
            var (source, target, window) = WideGrid(50);
            var scroll = target.Scroll!;
            scroll.Offset = new Vector(300, 0);
            Settle(window);

            // The first column (0..400) is visible from 300 to 400.
            Click(window, target.TryGetCell(0, 3)!.TranslatePoint(new Point(350, 5), window)!.Value);

            source.RowSelection!.SelectedIndex.Should().Be(new IndexPath(3));
            scroll.Offset.X.Should().Be(300);
        }

        // 12.2.1 (regression test): "Fix the expander chevron not updating when a row was expanded or collapsed by
        // double-click or keyboard" - keyboard.
        [AvaloniaFact(Timeout = 10000)]
        public void Expanding_and_collapsing_with_the_keyboard_updates_the_chevron()
        {
            var (source, target, window, _) = Tree();
            Click(window, target.TryGetCell(0, 0)!);
            var toggle = target.TryGetCell(0, 0)!.GetVisualDescendants().OfType<ToggleButton>().First();

            Press(window, PhysicalKey.ArrowRight);

            ((IExpander)source.Rows[0]).IsExpanded.Should().BeTrue();
            toggle.IsChecked.Should().BeTrue();

            Press(window, PhysicalKey.ArrowLeft);

            ((IExpander)source.Rows[0]).IsExpanded.Should().BeFalse();
            toggle.IsChecked.Should().BeFalse();
        }

        // 12.0.1 (regression test): "Expander row chevron double-tap flicker eliminated".
        [AvaloniaFact(Timeout = 10000)]
        public void Double_clicking_the_chevron_keeps_the_chevron_and_the_row_in_step()
        {
            var (source, target, window, _) = Tree();
            var toggle = target.TryGetCell(0, 0)!.GetVisualDescendants().OfType<ToggleButton>().First();

            Click(window, toggle);
            Click(window, toggle);

            toggle.IsChecked.Should().Be(((IExpander)source.Rows[0]).IsExpanded);
        }

        // Not in the notes (regression test): arrow keys in a cell editor do not move the row selection.
        [AvaloniaFact(Timeout = 10000)]
        public void Arrow_down_in_a_cell_editor_does_not_move_the_selection()
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(5)))
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, (m, v) => m.Title = v) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));
            var cell = (TreeDataGridTextCell)target.TryGetCell(0, 1)!;
            Click(window, cell);
            cell.BeginEdit();
            Settle(window);
            cell.GetVisualDescendants().OfType<TextBox>().Single().Focus();

            Press(window, PhysicalKey.ArrowDown);

            source.RowSelection!.SelectedIndex.Should().Be(new IndexPath(1));
            cell.IsEditing.Should().BeTrue();
        }

        // 12.3.0 (regression test): "Runtime column width setting now updates layout properly".
        [AvaloniaFact(Timeout = 10000)]
        public void Setting_a_column_width_at_runtime_updates_the_cells_and_the_header()
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(5)))
            {
                Columns =
                {
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(100)),
                    new TextColumn<Model, int>("Id", x => x.Id, new GridLength(100)),
                },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(500, 300));

            source.Columns.SetColumnWidth(0, new GridLength(200));
            Settle(window);

            target.TryGetCell(0, 0)!.Bounds.Width.Should().BeApproximately(200, 1);
            target.TryGetCell(1, 0)!.Bounds.X.Should().BeApproximately(200, 1);
            target.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().First().Bounds.Width
                .Should().BeApproximately(200, 1);
        }

        // 12.3.0, as above, through MinWidth and a new layout pass (what Signet's header fitting does).
        [AvaloniaFact(Timeout = 10000)]
        public void Raising_the_MinWidth_of_a_star_column_at_runtime_widens_its_cells()
        {
            var title = new TextColumn<Model, string?>("Title", x => x.Title, GridLength.Star);
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(5)))
            {
                Columns = { title, new TextColumn<Model, int>("Id", x => x.Id, new GridLength(100)) },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));

            title.Options.MinWidth = new GridLength(400);
            foreach (var presenter in target.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is TreeDataGridColumnHeadersPresenter or TreeDataGridRowsPresenter or TreeDataGridCellsPresenter))
                presenter.InvalidateMeasure();
            target.InvalidateMeasure();
            Settle(window);

            target.TryGetCell(0, 0)!.Bounds.Width.Should().BeGreaterThanOrEqualTo(399);
        }

        // 11.3.2 (regression test): "Fix crash on template cell visual tree detach".
        [AvaloniaFact(Timeout = 10000)]
        public void Detaching_and_reattaching_a_grid_with_template_cells_shows_the_current_items()
        {
            var items = new AvaloniaList<Model>(Items(50));
            var source = new FlatTreeDataGridSource<Model>(items)
            {
                Columns =
                {
                    new TemplateColumn<Model>("T", new FuncDataTemplate<Model>(
                        (_, _) => new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Model.Title)) }, true)),
                },
            };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 300));

            window.Content = null;
            Settle(window);
            items.RemoveRange(0, 10);
            items.Insert(0, new Model { Title = "New" });
            window.Content = target;
            Settle(window);
            window.Content = null;
            Settle(window);
            target.Source = null;
            window.Content = target;
            Settle(window);
            target.Source = source;
            Settle(window);

            target.TryGetCell(0, 0)!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("New");
        }

        // 12.0.2 (regression test): "Star-sized columns overflow prevented".
        [AvaloniaFact(Timeout = 10000)]
        public void Star_columns_fill_the_viewport_without_overflowing_it()
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(500)))
            {
                Columns =
                {
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(2, GridUnitType.Star)),
                    new TextColumn<Model, int>("Id", x => x.Id, GridLength.Star),
                },
            };
            var target = new TreeDataGrid { Source = source };
            ThemedWindow(target, new Size(300, 300));

            target.Scroll!.Extent.Width.Should().BeLessThanOrEqualTo(target.Scroll.Viewport.Width + 0.5);
        }

        // 12.0.3: "Multiple row selection drag collapse fixed". (Fixed here: a press-move-release counted as a click
        // when the pointer stayed within 3 px in either direction.)
        [AvaloniaFact(Timeout = 10000)]
        public void Pressing_on_a_multiple_selection_and_moving_vertically_keeps_the_selection()
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(20)))
            {
                Columns = { new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(200)) },
            };
            source.RowSelection!.SingleSelect = false;
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(300, 400));
            source.RowSelection.Select(new IndexPath(1));
            source.RowSelection.Select(new IndexPath(2));
            source.RowSelection.Select(new IndexPath(3));
            var from = CenterOf(target.TryGetCell(0, 2)!, window);
            var to = from + new Point(0, 80);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Settle(window);

            source.RowSelection.SelectedIndexes.Should().HaveCount(3);

            // A real click on one of them still selects only that row.
            Click(window, target.TryGetCell(0, 2)!);

            source.RowSelection.SelectedIndexes.Should().ContainSingle().Which.Should().Be(new IndexPath(2));
        }

        // 12.3.0 (regression test): "Corrected columns collection reporting incorrect indices on replace".
        [AvaloniaFact(Timeout = 10000)]
        public void Replacing_a_column_reports_its_index()
        {
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(3)))
            {
                Columns =
                {
                    new TextColumn<Model, string?>("A", x => x.Title),
                    new TextColumn<Model, string?>("B", x => x.Title),
                    new TextColumn<Model, string?>("C", x => x.Title),
                },
            };
            NotifyCollectionChangedEventArgs? args = null;
            ((INotifyCollectionChanged)source.Columns).CollectionChanged += (_, e) => args = e;

            source.Columns[1] = new TextColumn<Model, string?>("X", x => x.Title);

            args!.Action.Should().Be(NotifyCollectionChangedAction.Replace);
            args.NewStartingIndex.Should().Be(1);
            args.OldStartingIndex.Should().Be(1);
        }

        // Regression test for Signet's optional Book Browser columns: columns removed and inserted at runtime.
        [AvaloniaFact(Timeout = 10000)]
        public void Removing_and_inserting_columns_at_runtime_realizes_the_right_cells_and_headers()
        {
            var a = new TextColumn<Model, string?>("A", x => x.Title, new GridLength(100));
            var b = new TextColumn<Model, int>("B", x => x.Id, new GridLength(100));
            var c = new TextColumn<Model, string?>("C", x => "c" + x.Id, new GridLength(100));
            var source = new FlatTreeDataGridSource<Model>(new AvaloniaList<Model>(Items(5))) { Columns = { a, b, c } };
            var target = new TreeDataGrid { Source = source };
            var window = ThemedWindow(target, new Size(500, 300));

            source.Columns.Remove(b);
            Settle(window);

            ((TreeDataGridTextCell)target.TryGetCell(1, 2)!).Value.Should().Be("c2");

            source.Columns.Insert(1, b);
            Settle(window);

            ((TreeDataGridTextCell)target.TryGetCell(1, 2)!).Value.Should().Be("2");
            ((TreeDataGridTextCell)target.TryGetCell(2, 2)!).Value.Should().Be("c2");
            target.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Where(h => h.ColumnIndex >= 0)
                .OrderBy(h => h.ColumnIndex).Select(h => h.Header).Should().Equal("A", "B", "C");
        }
    }
}
