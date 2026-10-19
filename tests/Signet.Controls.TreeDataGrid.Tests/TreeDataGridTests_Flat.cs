using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Controls.Primitives;
using Signet.Controls.TreeDataGrid.Primitives;
using Avalonia.Controls.Selection;
using Signet.Controls.TreeDataGrid.Selection;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using Enumerable = System.Linq.Enumerable;

namespace Signet.Controls.TreeDataGrid.Tests
{
    public class TreeDataGridTests_Flat
    {
        [AvaloniaFact(Timeout = 10000)]
        public void Should_Display_Initial_Rows_And_Cells()
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
            rows.Count.Should().Be(10);

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
        public void MultiSelection_Should_Work_Correctly_With_Duplicates()
        {
            // Arrange
            var items = new List<Model>
            {
                new Model(){ Id=0, Title="Item 0"},
                new Model(){ Id=1, Title="Item 1"},
                new Model(){ Id=2, Title="Item 2"}
            };
            items.Add(items[0]);
            items.Add(items[0]);
            items.Add(items[0]);

            var (target, aaa) = CreateTarget(items);

            // Act
            target.RowSelection!.Select(3);
            target.RowSelection.Select(4);
            target.RowSelection.Select(5);

            // Assert
            AssertInteractionSelection(target, 3, 4, 5);

            // Act
            target.Source!.SortBy(target.Columns![0], ListSortDirection.Ascending);

            // Assert
            AssertInteractionSelection(target, 1, 2, 3);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Selection_Should_Be_Preserved_After_Sorting()
        {
            // Arrange
            var (target, aaa) = CreateTarget();

            // Act
            target.RowSelection!.Select(0);
            target.RowSelection.Select(5);

            // Assert
            AssertInteractionSelection(target, 0, 5);
            
            // Act
            target.Source!.SortBy(target.Columns![0], ListSortDirection.Descending);

            ///There are 100 items in the collection.
            ///Their IDs are in range 0..99 so when we order IDs column in Descending order the latest element of the collection would be with
            ///ID 0(index 99 in collection),first with ID 99
            // Assert
            AssertInteractionSelection(target, 94, 99);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Models_For_Initial_Rows()
        {
            // Arrange
            var (target, items) = CreateTarget();

            // Act & Assert
            for (var i = 0; i < items.Count; ++i)
            {
                var expected = i < 10 ? 2 : 0;
                items[i].PropertyChangedSubscriberCount().Should().Be(expected);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Correct_Models_After_Scrolling_Down_One_Row()
        {
            // Arrange
            var (target, items) = CreateTarget();

            // Act
            target.Scroll!.Offset = new Vector(0, 10);
            Layout(target);

            // Assert
            for (var i = 0; i < items.Count; ++i)
            {
                var expected = i > 0 && i <= 10 ? 2 : 0;
                items[i].PropertyChangedSubscriberCount().Should().Be(expected);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Subscribe_To_Correct_Models_After_Scrolling_Down_One_Page()
        {
            // Arrange
            var (target, items) = CreateTarget();

            // Act
            target.Scroll!.Offset = new Vector(0, 100);
            Layout(target);

            // Assert
            for (var i = 0; i < items.Count; ++i)
            {
                var expected = i >= 10 && i < 20 ? 2 : 0;
                items[i].PropertyChangedSubscriberCount().Should().Be(expected);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Unsubscribe_From_Models_When_Detached_From_Logical_Tree()
        {
            // Arrange
            var (target, items) = CreateTarget();

            // Act
            ((Window)target.Parent!).Content = null;

            // Assert
            for (var i = 0; i < items.Count; ++i)
            {
                items[i].PropertyChangedSubscriberCount().Should().Be(0);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Desired_Width_Should_Be_Total_Of_Fixed_Width_Columns()
        {
            // Arrange
            var (target, items) = CreateTarget(
                columns: new IColumn<Model>[]
                {
                    new TextColumn<Model, int>("ID", x => x.Id, new GridLength(10, GridUnitType.Pixel), MinWidth(0)),
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(14, GridUnitType.Pixel), MinWidth(0))
                }
            );

            // Act & Assert
            target.DesiredSize.Width.Should().Be(24);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Size_Star_Columns()
        {
            // Arrange
            var (target, items) = CreateTarget(
                columns: new IColumn<Model>[]
                {
                    new TextColumn<Model, int>("ID", x => x.Id, new GridLength(1, GridUnitType.Star), MinWidth(0)),
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(3, GridUnitType.Star), MinWidth(0))
                }
            );

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();

            // Assert
            rows.Count.Should().Be(10);

            foreach (var row in rows)
            {
                var cells = row.CellsPresenter!
                    .GetVisualChildren()
                    .Cast<TreeDataGridCell>()
                    .ToList();
                cells.Count.Should().Be(2);
                cells[0].Bounds.Width.Should().Be(25);
                cells[1].Bounds.Width.Should().Be(75);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Size_Star_Columns_With_Min_Width()
        {
            // Arrange
            var (target, items) = CreateTarget(
                columns: new IColumn<Model>[]
                {
                    new TextColumn<Model, int>("ID", x => x.Id, new GridLength(1, GridUnitType.Star), MinWidth(50)),
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(3, GridUnitType.Star))
                }
            );

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();

            // Assert
            rows.Count.Should().Be(10);

            foreach (var row in rows)
            {
                var cells = row.CellsPresenter!
                    .GetVisualChildren()
                    .Cast<TreeDataGridCell>()
                    .ToList();
                cells.Count.Should().Be(2);
                cells[0].Bounds.Width.Should().Be(50);
                cells[1].Bounds.Width.Should().Be(50);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Size_Star_Columns_With_Max_Width()
        {
            // Arrange
            var (target, items) = CreateTarget(
                columns: new IColumn<Model>[]
                {
                    new TextColumn<Model, int>("ID", x => x.Id, new GridLength(1, GridUnitType.Star)),
                    new TextColumn<Model, string?>("Title", x => x.Title, new GridLength(1, GridUnitType.Star), MaxWidth(25))
                }
            );

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();

            // Assert
            rows.Count.Should().Be(10);

            foreach (var row in rows)
            {
                var cells = row?.CellsPresenter!
                    .GetVisualChildren()
                    .Cast<TreeDataGridCell>()
                    .ToList();
                cells!.Count.Should().Be(2);
                cells[0].Bounds.Width.Should().Be(75);
                cells[1].Bounds.Width.Should().Be(25);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Raises_CellPrepared_Events_On_Initial_Layout()
        {
            // Arrange
            var (target, items) = CreateTarget(runLayout: false);
            var raised = 0;

            target.CellPrepared += (s, e) =>
            {
                ++raised;
            };

            // Act
            target.UpdateLayout();

            // Assert
            raised.Should().Be(20);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Raises_CellClearing_CellPrepared_Events_On_Scroll()
        {
            // Arrange
            var (target, items) = CreateTarget();
            var clearingRaised = 0;
            var preparedRaised = 0;

            target.CellClearing += (s, e) =>
            {
                e.ColumnIndex.Should().Be(clearingRaised % 2);
                e.RowIndex.Should().Be(0);
                ++clearingRaised;
            };

            target.CellPrepared += (s, e) =>
            {
                e.ColumnIndex.Should().Be(preparedRaised % 2);
                e.RowIndex.Should().Be(10);
                ++preparedRaised;
            };

            // Act
            target.Scroll!.Offset = new Vector(0, 10);
            Layout(target);

            // Assert
            clearingRaised.Should().Be(2);
            preparedRaised.Should().Be(2);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Raises_CellValueChanged_When_Model_Value_Changed()
        {
            // Arrange
            var (target, items) = CreateTarget();
            var raised = 0;

            target.CellValueChanged += (s, e) =>
            {
                e.ColumnIndex.Should().Be(1);
                e.RowIndex.Should().Be(1);
                ++raised;
            };

            // Act
            items[1].Title = "Changed";

            // Assert
            raised.Should().Be(1);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Raises_CellValueChanged_After_Cell_Edit()
        {
            // Arrange
            var (target, items) = CreateTarget();
            var raised = 0;

            target.CellValueChanged += (s, e) =>
            {
                e.ColumnIndex.Should().Be(1);
                e.RowIndex.Should().Be(1);
                ++raised;
            };

            // Act
            var cell = (target.TryGetRow(1)?.TryGetCell(1)).Should().BeOfType<TreeDataGridTextCell>().Subject;
            cell.BeginEdit();
            cell.Value = "Changed";

            // Assert
            raised.Should().Be(0);
            items[1].Title.Should().Be("Item 1");

            // Act
            cell.EndEdit();

            // Assert
            items[1].Title.Should().Be("Changed");
            raised.Should().Be(1);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Does_Not_Raise_CellValueChanged_Events_On_Initial_Layout()
        {
            // Arrange
            var (target, items) = CreateTarget(runLayout: false);
            var raised = 0;

            target.CellValueChanged += (s, e) => ++raised;

            // Act
            target.UpdateLayout();

            // Assert
            raised.Should().Be(0);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Does_Not_Raise_CellValueChanged_Events_On_Scroll()
        {
            // Arrange
            var (target, items) = CreateTarget();
            var raised = 0;

            target.CellValueChanged += (s, e) => ++raised;

            // Act
            target.Scroll!.Offset = new Vector(0, 10);
            Layout(target);

            // Assert
            raised.Should().Be(0);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Does_Not_Realize_Columns_Outside_Viewport()
        {
            // Arrange
            var (target, items) = CreateTarget(columns: new IColumn<Model>[]
            {
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>("Title1", x => x.Title, options: MinWidth(50)),
                new TextColumn<Model, string?>("Title2", x => x.Title, options: MinWidth(50)),
                new TextColumn<Model, string?>("Title3", x => x.Title, options: MinWidth(50)),
            });

            // Act & Assert
            AssertColumnIndexes(target, 0, 3);

            var columns = (ColumnList<Model>)target.Columns!;
            columns[0].ActualWidth.Should().Be(30);
            columns[1].ActualWidth.Should().Be(50);
            columns[2].ActualWidth.Should().Be(50);
            double.IsNaN(columns[3].ActualWidth).Should().BeTrue();
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Header_Column_Indexes_Are_Updated_When_Columns_Are_Updated()
        {
            // Arrange
            var (target, items) = CreateTarget(columns: new IColumn<Model>[]
            {
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>("Title1", x => x.Title,  width: new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>("Title2", x => x.Title,  width: new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>("Title3", x => x.Title,  width: new GridLength(1, GridUnitType.Star)),
            });

            // Act & Assert
            AssertColumnIndexes(target, 0, 4);

            var source =(FlatTreeDataGridSource<Model>)target.Source!;

            var movedColumn = source.Columns[1];

            // Act
            source.Columns.Remove(movedColumn);

            // Assert
            AssertColumnIndexes(target, 0, 3);

            // Act
            source.Columns.Add(movedColumn);

            var root = (TestWindow)TopLevel.GetTopLevel(target)!;
            root.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Assert
            AssertColumnIndexes(target, 0, 4);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Columns_Are_Correctly_Sized_After_Changing_Source()
        {
            // Create the initial target with 2 columns and make sure our preconditions are correct.
            // Arrange
            var (target, items) = CreateTarget(columns: new IColumn<Model>[]
            {
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>("Title1", x => x.Title, options: MinWidth(50)),
            });

            // Act & Assert
            AssertColumnIndexes(target, 0, 2);

            // Create a new source and assign it to the TreeDataGrid.
            // Act
            var newSource = new FlatTreeDataGridSource<Model>(items)
            {
                Columns =
                {
                    new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(1, GridUnitType.Star)),
                    new TextColumn<Model, string?>("Title1", x => x.Title, options: MinWidth(20)),
                    new TextColumn<Model, string?>("Title2", x => x.Title, options: MinWidth(20)),
                }
            };

            target.Source = newSource;

            // The columns should not have an ActualWidth yet.
            // Assert
            double.IsNaN(newSource.Columns[0].ActualWidth).Should().BeTrue();
            double.IsNaN(newSource.Columns[1].ActualWidth).Should().BeTrue();
            double.IsNaN(newSource.Columns[2].ActualWidth).Should().BeTrue();

            // Do a layout pass and check that the columns have been correctly sized.
            // Act
            target.UpdateLayout();

            // Assert
            AssertColumnIndexes(target, 0, 3);

            var columns = (ColumnList<Model>)target.Columns!;

            columns[0].ActualWidth.Should().Be(60);
            columns[1].ActualWidth.Should().Be(20);
            columns[2].ActualWidth.Should().Be(20);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Correctly_Align_Columns_When_Vertically_Scrolling_With_First_Column_Unrealized()
        {
            // Arrange
            // Issue #298
            static void AssertRealizedCells(TreeDataGrid target)
            {
                var rows = target.RowsPresenter!.GetVisualChildren().Cast<TreeDataGridRow>();

                foreach (var row in rows)
                {
                    var cells = row.CellsPresenter!.GetRealizedElements()
                        .Cast<TreeDataGridCell>()
                        .OrderBy(x => x.ColumnIndex)
                        .ToList();

                    cells.Count.Should().Be(3);
                    cells[0].ColumnIndex.Should().Be(1);
                    cells[0].Bounds.Left.Should().Be(100);
                    cells[1].Bounds.Left.Should().Be(150);
                    cells[2].Bounds.Left.Should().Be(200);
                }
            }

            var (target, items) = CreateTarget(columns:
            [
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(100, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title1", x => x.Title, width: new GridLength(50, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title2", x => x.Title, width: new GridLength(50, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title3", x => x.Title, width: new GridLength(50, GridUnitType.Pixel)),
            ]);

            // Scroll horizontally and check that the realized cells are positioned correctly.
            // Act
            target.Scroll!.Offset = new Vector(120, 0);
            target.UpdateLayout();

            // Assert
            AssertRealizedCells(target);

            // Scroll down a row and check that the realized cells are positioned correctly.
            // Act
            target.Scroll!.Offset = new Vector(120, 10);
            target.UpdateLayout();

            // Assert
            AssertRealizedCells(target);

            // Now scroll back vertically and check once more.
            // Act
            target.Scroll!.Offset = new Vector(120, 0);
            target.UpdateLayout();

            // Assert
            AssertRealizedCells(target);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Use_TextCell_StringFormat()
        {
            // Arrange
            var (target, items) = CreateTarget(columns: new IColumn<Model>[]
            {
                new TextColumn<Model, string?>("Title", x => x.Title, options: new()
                {
                    StringFormat = "Hello {0}"
                }),
            });

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();

            // Assert
            rows.Count.Should().Be(10);

            for (var i = 0; i < rows.Count; i++)
            {
                var cell = rows[i].CellsPresenter!.GetVisualChildren().Cast<TreeDataGridCell>().Should().ContainSingle().Subject.Should().BeOfType<TreeDataGridTextCell>().Subject;
                cell.Value.Should().Be($"Hello Item {i}");
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Use_TextCell_StringFormat_When_Model_Is_Updated()
        {
            // Arrange
            var (target, items) = CreateTarget(columns: new IColumn<Model>[]
            {
                new TextColumn<Model, string?>("Title", x => x.Title, options: new()
                {
                    StringFormat = "Hello {0}"
                }),
            });

            // Act
            var rows = target.RowsPresenter!
                .GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .ToList();

            // Assert
            rows.Count.Should().Be(10);

            // Act
            items[1].Title = "World";
            var cell = target.TryGetCell(0, 1).Should().BeOfType<TreeDataGridTextCell>().Subject;

            // Assert
            cell.Value.Should().Be("Hello World");
        }

        public class RemoveItems
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Within_Realized_Elements()
            {
                // Arrange
                var (target, items) = CreateTarget();

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(12, 4);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 100));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Within_Realized_Elements_When_Scrolled_To_End()
            {
                // Arrange
                var (target, items) = CreateTarget(itemCount: 20);

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(12, 4);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 6, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 60));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Of_All_Realized_Elements()
            {
                // Arrange
                var (target, items) = CreateTarget();

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(10, 10);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 100));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Of_All_Realized_Elements_When_Scrolled_To_End()
            {
                // Arrange
                var (target, items) = CreateTarget(itemCount: 20);

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(10, 10);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 0, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 0));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Spanning_Beginning_Of_Realized_Elements_When_Scrolled_To_End()
            {
                // Arrange
                var (target, items) = CreateTarget(itemCount: 20);

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(5, 10);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 0, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 0));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Range_Spanning_End_Of_Realized_Elements()
            {
                // Arrange
                var (target, items) = CreateTarget();

                // Act
                target.Scroll!.Offset = new Vector(0, 100);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);

                // Act
                items.RemoveRange(15, 10);
                Layout(target);

                // Assert
                AssertRowIndexes(target, 10, 10);
                target.Scroll.Offset.Should().Be(new Vector(0, 100));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Selected_Item()
            {
                // Arrange
                var (target, items) = CreateTarget();

                // Act
                Layout(target);
                target.RowSelection!.Select(3);

                // Assert
                target.RowSelection.SelectedIndex.Should().Be(3);

                // Act
                items.RemoveAt(3);

                // Assert
                target.RowSelection.SelectedIndex.Should().Be(-1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Remove_Selected_Item_Sorted()
            {
                // Arrange
                var (target, items) = CreateTarget();

                // Act
                target.Source!.SortBy(target.Columns![0], ListSortDirection.Descending);

                Layout(target);
                target.RowSelection!.Select(3);

                // Assert
                target.RowSelection.SelectedIndex.Should().Be(3);

                // Act
                items.RemoveAt(3);

                // Assert
                target.RowSelection.SelectedIndex.Should().Be(-1);
            }
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Show_Horizontal_ScrollBar()
        {
            // Arrange
            var (target, items) = CreateTarget(columns:
            [
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(100, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title1", x => x.Title,  width: new GridLength(100, GridUnitType.Pixel)),
            ]);

            // Act & Assert
            var scroll = target.Scroll.Should().BeOfType<ScrollViewer>().Subject;
            var headerScroll = (target.GetVisualDescendants().Single(x => x.Name == "PART_HeaderScrollViewer")).Should().BeOfType<ScrollViewer>().Subject;

            scroll.Viewport.Should().Be(new Size(100, 100));
            scroll.Extent.Should().Be(new Size(200, 1000));
            headerScroll.Viewport.Should().Be(new Size(100, 0));
            headerScroll.Extent.Should().Be(new Size(200, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Show_Horizontal_ScrollBar_With_No_Initial_Rows()
        {
            // Arrange
            var (target, items) = CreateTarget(columns:
            [
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(100, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title1", x => x.Title,  width: new GridLength(100, GridUnitType.Pixel)),
            ], itemCount: 0);

            // Act & Assert
            var scroll = target.Scroll.Should().BeOfType<ScrollViewer>().Subject;
            var headerScroll = (target.GetVisualDescendants().Single(x => x.Name == "PART_HeaderScrollViewer")).Should().BeOfType<ScrollViewer>().Subject;

            scroll.Viewport.Should().Be(new Size(100, 100));
            scroll.Extent.Should().Be(new Size(200, 100));
            headerScroll.Viewport.Should().Be(new Size(100, 0));
            headerScroll.Extent.Should().Be(new Size(200, 0));
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Should_Preserve_Horizontal_ScrollBar_When_Rows_Removed()
        {
            // Arrange
            var (target, items) = CreateTarget(columns:
            [
                new TextColumn<Model, int>("ID", x => x.Id, width: new GridLength(100, GridUnitType.Pixel)),
                new TextColumn<Model, string?>("Title1", x => x.Title,  width: new GridLength(100, GridUnitType.Pixel)),
            ]);
            var scroll = target.Scroll.Should().BeOfType<ScrollViewer>().Subject;
            var headerScroll = (target.GetVisualDescendants().Single(x => x.Name == "PART_HeaderScrollViewer")).Should().BeOfType<ScrollViewer>().Subject;

            scroll.PropertyChanged += (s, e) => 
            { 
                if (e.Property == ScrollViewer.ExtentProperty)
                {
                }
            };

            // Act
            items.Clear();
            target.UpdateLayout();

            // Assert
            scroll.Viewport.Should().Be(new Size(100, 100));
            scroll.Extent.Should().Be(new Size(200, 100));
            headerScroll.Viewport.Should().Be(new Size(100, 0));
            headerScroll.Extent.Should().Be(new Size(200, 0));
        }

        private static void AssertRowIndexes(TreeDataGrid target, int firstRowIndex, int rowCount)
        {
            var presenter = target.RowsPresenter;

            presenter.Should().NotBeNull();

            var rowIndexes = presenter?.GetVisualChildren()
                .Cast<TreeDataGridRow>()
                .Where(x => x.IsVisible)
                .Select(x => x.RowIndex)
                .OrderBy(x => x)
                .ToList();

            rowIndexes.Should().Equal(Enumerable.Range(firstRowIndex, rowCount));

            rowIndexes = presenter!.RealizedElements
                .Cast<TreeDataGridRow>()
                .Where(x => x.IsVisible)
                .Select(x => x.RowIndex)
                .OrderBy(x => x)
                .ToList();

            rowIndexes.Should().Equal(Enumerable.Range(firstRowIndex, rowCount));
        }

        private static void AssertColumnIndexes(TreeDataGrid target, int firstColumnIndex, int columnCount)
        {
            var presenter = target.ColumnHeadersPresenter;

            presenter.Should().NotBeNull();

            var columnIndexes = presenter?.GetVisualChildren()
                .Cast<TreeDataGridColumnHeader>()
                .Where(x => x.IsVisible)
                .Select(x => x.ColumnIndex)
                .OrderBy(x => x)
                .ToList();

            columnIndexes.Should().Equal(Enumerable.Range(firstColumnIndex, columnCount));

            columnIndexes = presenter!.RealizedElements
                .Cast<TreeDataGridColumnHeader>()
                .Where(x => x.IsVisible)
                .Select(x => x.ColumnIndex)
                .OrderBy(x => x)
                .ToList();

            columnIndexes.Should().Equal(Enumerable.Range(firstColumnIndex, columnCount));
        }

        private static void AssertInteractionSelection(TreeDataGrid target, params int[] selected)
        {
            var selection = (ITreeDataGridSelectionInteraction)target.RowSelection!;

            for (var i = 0; i < target.Rows!.Count; ++i)
            {
                selection.IsRowSelected(i).Should().Be(selected.Contains(i));
            }
        }

        private static (TreeDataGrid, AvaloniaList<Model>) CreateTarget(IEnumerable<Model>? models = null,
            IEnumerable<IColumn<Model>>? columns = null,
            int itemCount = 100,
            bool runLayout = true)
        {
            AvaloniaList<Model>? items = null;
            if (models == null)
            {
                items = new AvaloniaList<Model>(Enumerable.Range(0, itemCount).Select(x =>
                    new Model
                    {
                        Id = x,
                        Title = "Item " + x,
                    }));
            }
            else
            {
                items = new AvaloniaList<Model>(models);
            }


            var source = new FlatTreeDataGridSource<Model>(items);
            source.RowSelection!.SingleSelect = false;

            if (columns is object)
            {
                foreach (var column in columns)
                    source.Columns.Add(column);
            }
            else
            {
                source.Columns.Add(new TextColumn<Model, int>("ID", x => x.Id));
                source.Columns.Add(new TextColumn<Model, string?>("Title", x => x.Title, (o, v) => o.Title = v));
            }

            var target = new TreeDataGrid
            {
                Template = TestTemplates.TreeDataGridTemplate(),
                Source = source,
            };

            var root = new TestWindow(target)
            {
                Styles =
                {
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
            {
                root.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            return (target, items);
        }

        private static void Layout(TreeDataGrid target)
        {
            target.UpdateLayout();
        }

        private static TextColumnOptions<Model> MinWidth(double min)
        {
            return new TextColumnOptions<Model>
            {
                MinWidth = new GridLength(min, GridUnitType.Pixel),
            };
        }

        private static TextColumnOptions<Model> MaxWidth(double max)
        {
            return new TextColumnOptions<Model>
            {
                MaxWidth = new GridLength(max, GridUnitType.Pixel),
            };
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
        }
    }
}
