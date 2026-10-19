using AwesomeAssertions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Controls.Primitives;
using Signet.Controls.TreeDataGrid.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests
{
    public class TreeDataGridTests_Hierarchical
    {
        [AvaloniaFact(Timeout = 10000)]
        public void Should_Display_Initial_Row_And_Cells()
        {
            // Arrange
            var (target, _) = CreateTarget();

            target.RowsPresenter.Should().NotBeNull();

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();
            
            // Assert
            rows.Count.Should().Be(2);

            foreach (var row in rows)
            {
                var cells = row.CellsPresenter!
                    .GetVisualChildren()
                    .Cast<TreeDataGridCell>()
                    .ToList();
                cells.Count.Should().Be(2);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Display_Expanded_Root_Node()
        {
            // Arrange
            var (target, source) = CreateTarget();

            target.RowsPresenter.Should().NotBeNull();
            target.RowsPresenter!.RealizedElements.Count.Should().Be(2);
            target.RowsPresenter!.GetVisualChildren().Count().Should().Be(2);

            // Act
            source.Expand(new IndexPath(0));

            // Assert
            source.Rows.Count.Should().Be(102);
            target.RowsPresenter!.RealizedElements.Count.Should().Be(102);
            target.RowsPresenter!.GetVisualChildren().Count().Should().Be(2);

            // Act
            Layout(target);

            // Assert
            target.RowsPresenter!.RealizedElements.Count.Should().Be(10);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Display_Added_Root_Node()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;

            // Act
            Layout(target);
            items.Add(new Model { Id = -1, Title = "Added" });
            Layout(target);

            // Assert
            target.RowsPresenter!.RealizedElements.Count.Should().Be(3);
            target.RowsPresenter!.GetVisualChildren().Count().Should().Be(3);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Display_Added_Child_Node()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;
            var children = items[1].Children = new AvaloniaList<Model>
            {
                new Model { Id = -1, Title = "First" }
            };

            // Act
            Layout(target);
            source.Expand(new IndexPath(1));
            Layout(target);
            children.Add(new Model { Id = -2, Title = "Second" });
            Layout(target);

            // Assert
            target.RowsPresenter!.RealizedElements.Count.Should().Be(4);
            target.RowsPresenter!.GetVisualChildren().Count().Should().Be(4);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void RowIndexes_Should_Be_Correct_After_Expanding_Node_While_Scrolled()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;
            var children = items[0].Children![1].Children = new AvaloniaList<Model>
            {
                new Model { Id = -1, Title = "First" }
            };

            // Act
            source.Expand(0);
            target.Scroll!.Offset = new Vector(0, 20);
            Layout(target);
            
            var rowIndexes = target.RowsPresenter!.RealizedElements
                .OfType<TreeDataGridRow>()
                .Select(x => x.RowIndex)
                .ToList();

            // Assert
            rowIndexes.Should().Equal(Enumerable.Range(2, 10));

            // Act
            source.Expand(new IndexPath(0, 1));
            Layout(target);

            rowIndexes = target.RowsPresenter!.RealizedElements
                .OfType<TreeDataGridRow>()
                .Select(x => x.RowIndex)
                .ToList();

            // Assert
            rowIndexes.Should().Equal(Enumerable.Range(2, 10));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Models_For_Initial_Rows()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;

            // Act & Assert
            for (var i = 0; i < items.Count; ++i)
            {
                items[i].PropertyChangedSubscriberCount().Should().Be(2);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Models_For_Expanded_Rows()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;

            // Act
            source.Expand(new IndexPath(0));
            Layout(target);

            // Assert
            items[0].PropertyChangedSubscriberCount().Should().Be(2);
            items[1].PropertyChangedSubscriberCount().Should().Be(0);

            var children = items[0].Children!;
            for (var i = 0; i < children.Count; ++i)
            {
                var expected = i < 9 ? 2 : 0;
                children[i].PropertyChangedSubscriberCount().Should().Be(expected);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Correct_Models_After_Scrolling_Down_One_Row()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;

            // Act
            source.Expand(new IndexPath(0));
            Layout(target);
            target.Scroll!.Offset = new Vector(0, 10);
            Layout(target);

            // Assert
            items[0].PropertyChangedSubscriberCount().Should().Be(0);
            items[1].PropertyChangedSubscriberCount().Should().Be(0);

            var children = items[0].Children!;
            for (var i = 0; i < children.Count; ++i)
            {
                var expected = i < 10 ? 2 : 0;
                children[i].PropertyChangedSubscriberCount().Should().Be(expected);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Scrolling_Should_Not_Rebuild_Templates_In_Expander_Columns()
        {
            // Arrange
            var instantiations = 0;

            Control Template(Model model, INameScope ns)
            {
                ++instantiations;
                return new Border();
            }

            var columns = new IColumn<Model>[]
            {
                new HierarchicalExpanderColumn<Model>(
                    new TemplateColumn<Model>("ID", new FuncDataTemplate<Model>(Template, true)),
                    x => x.Children,
                    x => true),
                new TextColumn<Model, string?>("Title", x => x.Title),
            };

            // Create the TreeDataGrid but don't do an initial layout.
            var (target, source) = CreateTarget(columns: columns, runLayout: false);
            var items = (IList<Model>)source.Items;

            // Expand the first root and do the initial layout now.
            instantiations = 0;

            // Act
            source.Expand(new IndexPath(0));
            InitialLayout(target);

            // Assert
            instantiations.Should().Be(9);

            // Scroll down a row.
            // Act
            target.Scroll!.Offset = new Vector(0, 10);
            Layout(target);

            // Template should have been recycled and not rebuilt.
            // Assert
            instantiations.Should().Be(9);
            target.RowsPresenter!.RealizedElements.Count.Should().Be(10);

            for (var i = 0; i < 10; ++i)
            {
                var row = (TreeDataGridRow)target.RowsPresenter!.RealizedElements[i]!;
                var cell = (TreeDataGridExpanderCell)row.CellsPresenter!.RealizedElements[0]!;
                var inner = cell.FindDescendantOfType<TreeDataGridTemplateCell>()!;
                var innerModel = (TemplateCell)inner.DataContext!;
                var rowModel = source.Rows[i + 1].Model;

                row.DataContext.Should().Be(rowModel);
                cell.DataContext.Should().Be(rowModel);
                innerModel.Value.Should().Be(rowModel);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Unsubscribe_From_Models_When_Detached_From_Logical_Tree()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var items = (IList<Model>)source.Items;

            // Act
            ((Window)target.Parent!).Content = null;

            // Assert
            for (var i = 0; i < items.Count; ++i)
            {
                items[i].PropertyChangedSubscriberCount().Should().Be(0);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Hide_Expander_When_Node_With_No_Children_Expanded()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var cell = target.TryGetCell(0, 1);
            var expander = cell.Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            expander.IsExpanded.Should().BeFalse();
            expander.ShowExpander.Should().BeTrue();

            // Act
            expander.IsExpanded = true;

            // Assert
            expander.IsExpanded.Should().BeFalse();
            expander.ShowExpander.Should().BeFalse();
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Reassign_Items_When_Displaying_Child_Items_Followed_By_Root_Items()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var cell = target.TryGetCell(0, 0);
            var expander = cell.Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            // Add a a few more root items.
            ((AvaloniaList<Model>)source.Items).AddRange(CreateModels("Root ", 5, firstIndex: 2));

            // Expand the first root item and scroll down such that we're displaying some children
            // of the first root item together with subsequent root items.
            // Act
            source.Expand(new IndexPath(0));
            Layout(target);
            target.Scroll!.Offset = new Vector(0, 1970);
            Layout(target);

            var firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.First()!;
            var lastRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.Last()!;
            var firstRowModel = (IRow<Model>)source.Rows[firstRow.RowIndex];
            var lastRowModel = (IRow<Model>)source.Rows[lastRow.RowIndex];

            // Assert
            firstRowModel.Model.Title.Should().Be("Item 0-96");
            lastRowModel.Model.Title.Should().Be("Root 6");

            // Replace the items with a single item.
            // Act
            source.Items = new AvaloniaList<Model>
            {
                new Model
                {
                    Id = 0,
                    Title = "Root 0",
                },
            };

            Layout(target);

            firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements[0]!;

            // Assert
            firstRow.RowIndex.Should().Be(0);
            target.Scroll!.Offset.Should().Be(new Vector(0, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Reassign_Items_When_Displaying_Grandchild_Items_Followed_By_Root_Items()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var cell = target.TryGetCell(0, 0);
            var expander = cell.Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            // Add a a few more root items.
            ((AvaloniaList<Model>)source.Items).AddRange(CreateModels("Root ", 5, firstIndex: 2));

            // Add some grandchildren.
            ((AvaloniaList<Model>)source.Items)[0].Children!.AddRange(CreateModels("Item 0-0-", 100));

            // Expand the first child item and scroll down such that we're displaying some children
            // of the first root item together with subsequent root items.
            // Act
            source.Expand(new IndexPath(0, 0));
            Layout(target);
            target.Scroll!.Offset = new Vector(0, 1970);
            Layout(target);

            var firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.First()!;
            var lastRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.Last()!;
            var firstRowModel = (IRow<Model>)source.Rows[firstRow.RowIndex];
            var lastRowModel = (IRow<Model>)source.Rows[lastRow.RowIndex];

            // Assert
            firstRowModel.Model.Title.Should().Be("Item 0-0-96");
            lastRowModel.Model.Title.Should().Be("Root 6");

            // Replace the items with a single item.
            // Act
            source.Items = new AvaloniaList<Model>
            {
                new Model
                {
                    Id = 0,
                    Title = "Root 0",
                },
            };

            Layout(target);

            firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements[0]!;

            // Assert
            firstRow.RowIndex.Should().Be(0);
            target.Scroll!.Offset.Should().Be(new Vector(0, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Reset_Items_When_Displaying_Child_Items_Followed_By_Root_Items()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var cell = target.TryGetCell(0, 0);
            var expander = cell.Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            // Add a a few more root items.
            ((AvaloniaList<Model>)source.Items).AddRange(CreateModels("Root ", 5, firstIndex: 2));

            // Expand the first root item and scroll down such that we're displaying some children
            // of the first root item together with subsequent root items.
            // Act
            source.Expand(new IndexPath(0));
            Layout(target);
            target.Scroll!.Offset = new Vector(0, 1970);
            Layout(target);

            var firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.First()!;
            var lastRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.Last()!;
            var firstRowModel = (IRow<Model>)source.Rows[firstRow.RowIndex];
            var lastRowModel = (IRow<Model>)source.Rows[lastRow.RowIndex];

            // Assert
            firstRowModel.Model.Title.Should().Be("Item 0-96");
            lastRowModel.Model.Title.Should().Be("Root 6");

            // Clear the items.
            // Act
            ((IList)source.Items).Clear();

            Layout(target);

            // Assert
            target.RowsPresenter!.RealizedElements.Should().BeEmpty();
            target.Scroll!.Offset.Should().Be(new Vector(0, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Reset_Child_Items_When_Displaying_Grandchild_Items_Followed_By_Root_Items()
        {
            // Arrange
            var (target, source) = CreateTarget();
            var cell = target.TryGetCell(0, 0);
            var expander = cell.Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            // Add a a few more root items.
            ((AvaloniaList<Model>)source.Items).AddRange(CreateModels("Root ", 5, firstIndex: 2));

            // Add some grandchildren.
            ((AvaloniaList<Model>)source.Items)[0].Children!.AddRange(CreateModels("Item 0-0-", 100));

            // Expand the first child item and scroll down such that we're displaying some children
            // of the first root item together with subsequent root items.
            // Act
            source.Expand(new IndexPath(0, 0));
            Layout(target);
            target.Scroll!.Offset = new Vector(0, 1970);
            Layout(target);

            var firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.First()!;
            var lastRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements.Last()!;
            var firstRowModel = (IRow<Model>)source.Rows[firstRow.RowIndex];
            var lastRowModel = (IRow<Model>)source.Rows[lastRow.RowIndex];

            // Assert
            firstRowModel.Model.Title.Should().Be("Item 0-0-96");
            lastRowModel.Model.Title.Should().Be("Root 6");

            // Clear the child items.
            // Act
            ((AvaloniaList<Model>)source.Items)[0].Children!.Clear();

            Layout(target);

            firstRow = (TreeDataGridRow)target.RowsPresenter!.RealizedElements[0]!;

            // Assert
            firstRow.RowIndex.Should().Be(0);
            target.Scroll!.Offset.Should().Be(new Vector(0, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Remove_Selected_Item()
        {
            // Arrange
            var (target, source) = CreateTarget();

            // Act
            source.Expand(new IndexPath(0, 0));
            Layout(target);
            target.RowSelection!.Select(new IndexPath(0, 3));

            // Assert
            target.RowSelection.SelectedIndex.Should().Be(new IndexPath(0, 3));

            // Act
            ((AvaloniaList<Model>)source.Items)[0].Children!.RemoveAt(3);

            // Assert
            target.RowSelection.SelectedIndex.Should().Be(-1);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Remove_Selected_Item_Sorted()
        {
            // Arrange
            var (target, source) = CreateTarget();

            // Act
            target.Source!.SortBy(target.Columns![0], ListSortDirection.Descending);

            source.Expand(new IndexPath(0, 0));
            Layout(target);
            target.RowSelection!.Select(new IndexPath(0, 3));

            // Assert
            target.RowSelection.SelectedIndex.Should().Be(new IndexPath(0, 3));

            // Act
            ((AvaloniaList<Model>)source.Items)[0].Children!.RemoveAt(3);

            // Assert
            target.RowSelection.SelectedIndex.Should().Be(-1);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Recycle_Focused_Cell_When_Row_Collapsed()
        {
            // Arrange
            // Issue #210.
            var (target, source) = CreateTarget();

            // Act
            source.Expand(new IndexPath(0));
            Layout(target);

            // Assert
            target.RowsPresenter!.RealizedElements.Count.Should().Be(10);
            var row = target.TryGetRow(1).Should().BeOfType<TreeDataGridRow>().Subject;
            var cell = row.TryGetCell(0).Should().BeOfType<TreeDataGridExpanderCell>().Subject;

            cell.RowIndex.Should().Be(1);
            
            // Act
            cell.Focus();

            source.Collapse(new IndexPath(0));

            // Assert
            row.RowIndex.Should().Be(-1);

            // At this point, the cell should have been recycled and should have a row index of -1,
            // otherwise it can may recyled in a subsequent layout pass and the cell will not
            // be updated correctly.
            cell.RowIndex.Should().Be(-1);
        }

        private static (TreeDataGrid, HierarchicalTreeDataGridSource<Model>) CreateTarget(
            IEnumerable<IColumn<Model>>? columns = null,
            bool runLayout = true)
        {
            var items = new AvaloniaList<Model>
            {
                new Model
                {
                    Id = 0,
                    Title = "Root 0",
                    Children = new AvaloniaList<Model>(CreateModels("Item 0-", 100))
                },
                new Model
                {
                    Id = 1,
                    Title = "Root 1",
                },
            };

            columns ??= new IColumn<Model>[]
            {
                new HierarchicalExpanderColumn<Model>(
                    new TextColumn<Model, int>("ID", x => x.Id),
                    x => x.Children,
                    x => true),
                new TextColumn<Model, string?>("Title", x => x.Title),
            };

            var source = new HierarchicalTreeDataGridSource<Model>(items);
            source.Columns.AddRange(columns);

            var target = new TreeDataGrid
            {
                Template = TestTemplates.TreeDataGridTemplate(),
                Source = source,
            };

            var root = new TestWindow(target)
            {
                Styles =
                {
                    TestTemplates.TreeDataGridExpanderCellStyle,
                    TestTemplates.TreeDataGridTemplateCellStyle,
                    new Style(x => x.Is<TreeDataGridRow>())
                    {
                        Setters =
                        {
                            new Setter(TreeDataGridRow.TemplateProperty, TestTemplates.TreeDataGridRowTemplate()),
                        }
                    },
                    new Style(x => x.Is<TreeDataGridCell>())
                    {
                        Setters =
                        {
                            new Setter(TreeDataGridCell.HeightProperty, 10.0),
                        }
                    }
                }
            };

            if (runLayout)
                root.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            return (target, source);
        }

        private static void Layout(TreeDataGrid target)
        {
            target.UpdateLayout();
        }

        private static void InitialLayout(TreeDataGrid target)
        {
            target.UpdateLayout();
        }

        private static IEnumerable<Model> CreateModels(
            string titlePrefix,
            int count,
            int firstIndex = 0,
            int firstId = 100)
        {
            return Enumerable.Range(0, count).Select(x =>
                new Model
                {
                    Id = firstId + firstIndex + x,
                    Title = titlePrefix + (firstIndex + x),
                });
        }

        private class Model : NotifyingBase
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public AvaloniaList<Model>? Children { get; set; }
        }
    }
}
