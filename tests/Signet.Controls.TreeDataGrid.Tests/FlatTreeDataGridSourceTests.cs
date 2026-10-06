using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Controls.Selection;
using Signet.Controls.TreeDataGrid.Selection;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests
{
    public class FlatTreeDataGridSourceTests
    {
        [AvaloniaFact(Timeout = 10000)]
        public void Creates_Initial_Rows()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Supports_Adding_Row()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            target.Rows.Count.Should().Be(10);

            var raised = 0;
            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Add);
                e.NewStartingIndex.Should().Be(10);
                ++raised;
            };

            data.Add(new Row { Id = 10, Caption = "New Row 10" });

            target.Rows.Count.Should().Be(11);
            raised.Should().Be(1);

            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Supports_Removing_Row()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            target.Rows.Count.Should().Be(10);

            var raised = 0;
            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Remove);
                e.OldStartingIndex.Should().Be(5);
                ++raised;
            };

            data.RemoveAt(5);

            raised.Should().Be(1);
            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Supports_Replacing_Row()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            target.Rows.Count.Should().Be(10);

            var raised = 0;
            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Replace);
                e.NewStartingIndex.Should().Be(5);
                e.OldStartingIndex.Should().Be(5);
                ++raised;
            };

            data[5] = new Row { Id = 10, Caption = "New Row 10" };

            raised.Should().Be(1);
            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Supports_Moving_Row()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            target.Rows.Count.Should().Be(10);

            var raised = 0;
            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Move);
                e.NewStartingIndex.Should().Be(8);
                e.OldStartingIndex.Should().Be(5);
                ++raised;
            };

            data.Move(5, 8);

            raised.Should().Be(1);
            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Supports_Clearing_Rows()
        {
            var data = CreateData();
            var target = CreateTarget(data);

            target.Rows.Count.Should().Be(10);

            var raised = 0;
            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
                ++raised;
            };

            data.Clear();

            raised.Should().Be(1);
            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Can_Reassign_Items()
        {
            var data = CreateData();
            var target = CreateTarget(data);
            var raised = 0;

            AssertRows(target.Rows, data);

            target.Rows.CollectionChanged += (s, e) =>
            {
                e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
                ++raised;
            };

            target.Items = data = CreateData(20);

            raised.Should().Be(1);
            AssertRows(target.Rows, data);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Raises_Rows_Reset_When_Reassigning_Items_But_Rows_Not_Yet_Read()
        {
            var data = CreateData();
            var target = CreateTarget(data);
            var raised = 0;

            target.Rows.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                    ++raised;
            };

            target.Items = CreateData();

            raised.Should().Be(1);
        }

        public class Sorted
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Sorts_Initial_Cells()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                target.Rows.Count.Should().Be(10);

                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Supports_Adding_Row()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                AssertRows(target.Rows, data);

                var raised = 0;
                target.Rows.CollectionChanged += (s, e) =>
                {
                    e.Action.Should().Be(NotifyCollectionChangedAction.Add);
                    e.NewStartingIndex.Should().Be(0);
                    e.NewItems!.Cast<object>().Should().ContainSingle();
                    (((IModelIndexableRow)e.NewItems![0]!).ModelIndex).Should().Be(10);
                    ++raised;
                };

                data.Add(new Row { Id = 10, Caption = "New Row 10" });

                target.Rows.Count.Should().Be(11);
                raised.Should().Be(1);

                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Supports_Removing_Row()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                AssertRows(target.Rows, data);

                var raised = 0;
                target.Rows.CollectionChanged += (s, e) =>
                {
                    e.Action.Should().Be(NotifyCollectionChangedAction.Remove);
                    e.OldStartingIndex.Should().Be(4);
                    e.OldItems!.Cast<object>().Should().ContainSingle();
                    (((IModelIndexableRow)e.OldItems![0]!).ModelIndex).Should().Be(5);
                    ++raised;
                };

                data.RemoveAt(5);

                raised.Should().Be(1);
                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Supports_Replacing_Row()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                AssertRows(target.Rows, data);

                var raised = 0;
                target.Rows.CollectionChanged += (s, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                        e.OldStartingIndex.Should().Be(4);
                    else if (e.Action == NotifyCollectionChangedAction.Add)
                        e.NewStartingIndex.Should().Be(0);
                    else
                        Assert.Fail("Unexpected collection change");
                    ++raised;
                };

                data[5] = new Row { Id = 10, Caption = "New Row 10" };

                raised.Should().Be(2);
                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Supports_Moving_Row()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                AssertRows(target.Rows, data);

                var raised = 0;
                target.Rows.CollectionChanged += (s, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                        e.OldStartingIndex.Should().Be(4);
                    else if (e.Action == NotifyCollectionChangedAction.Add)
                        e.NewStartingIndex.Should().Be(4);
                    else
                        Assert.Fail("Unexpected collection change");
                    ++raised;
                };

                data.Move(5, 8);

                raised.Should().Be(2);
                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Supports_Clearing_Rows()
            {
                var data = CreateData();
                var target = CreateTarget(data);

                AssertRows(target.Rows, data);

                var raised = 0;
                target.Rows.CollectionChanged += (s, e) =>
                {
                    e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
                    ++raised;
                };

                data.Clear();

                raised.Should().Be(1);
                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Reassign_Items()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var raised = 0;

                AssertRows(target.Rows, data);

                target.Rows.CollectionChanged += (s, e) =>
                {
                    e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
                    ++raised;
                };

                target.Items = data = CreateData(20);

                raised.Should().Be(1);
                AssertRows(target.Rows, data);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Raises_Rows_Reset_When_Reassigning_Items_But_Rows_Not_Yet_Read()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var raised = 0;

                target.Rows.CollectionChanged += (s, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Reset)
                        ++raised;
                };

                target.Items = CreateData();

                raised.Should().Be(1);
            }

            private static FlatTreeDataGridSource<Row> CreateTarget(IEnumerable<Row> rows)
            {
                var result = FlatTreeDataGridSourceTests.CreateTarget(rows);
                ((AnonymousSortableRows<Row>)result.Rows).Sort(new FuncComparer<Row>(
                    new Comparison<Row?>((x, y) => (y?.Id ?? 0) - (x?.Id ?? 0))));
                return result;
            }

            private static void AssertRows(IRows rows, IList<Row> data)
            {
                rows.Count.Should().Be(data.Count);

                var sortedData = data.OrderByDescending(x => x.Id).ToList();

                for (var i = 0; i < data.Count; ++i)
                {
                    var row = (IRow<Row>)rows[i];
                    var indexable = (IModelIndexableRow)row;
                    row.Model.Should().BeSameAs(sortedData[i]);
                    indexable.ModelIndex.Should().Be(data.IndexOf(row.Model));
                }
            }
        }

        public class Selection
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Reassigning_Source_Updates_Selection_Model_Source()
            {
                var data1 = CreateData();
                var data2 = CreateData(5);
                var target = CreateTarget(data1);

                // Ensure selection model is created.
                (((ITreeDataGridSelection?)target.RowSelection)!.Source).Should().BeSameAs(data1);

                target.Items = data2;

                (((ITreeDataGridSelection?)target.RowSelection)!.Source).Should().BeSameAs(data2);
            }
        }

        private static FlatTreeDataGridSource<Row> CreateTarget(IEnumerable<Row> rows)
        {
            return new FlatTreeDataGridSource<Row>(rows)
            {
                Columns =
                {
                    new TextColumn<Row, int>("ID", x => x.Id),
                    new TextColumn<Row, string?>("Caption", x => x.Caption),
                }
            };
        }

        private static AvaloniaList<Row> CreateData(int count = 10)
        {
            var rows = Enumerable.Range(0, count).Select(x => new Row { Id = x, Caption = $"Row {x}" });
            return new AvaloniaList<Row>(rows);
        }

        private static void AssertRows(IRows rows, IList<Row> data)
        {
            rows.Count.Should().Be(data.Count);

            for (var i = 0; i < data.Count; ++i)
            {
                var row = (IRow<Row>)rows[i];
                var indexable = (IModelIndexableRow)row;
                data[i].Should().BeSameAs(row.Model);
                indexable.ModelIndex.Should().Be(i);
            }
        }

        private class Row : NotifyingBase
        {
            private int _id;
            private string? _caption;

            public int Id 
            {
                get => _id;
                set => RaiseAndSetIfChanged(ref _id, value);
            }

            public string? Caption 
            {
                get => _caption;
                set => RaiseAndSetIfChanged(ref _caption, value);
            }
        }
    }
}
