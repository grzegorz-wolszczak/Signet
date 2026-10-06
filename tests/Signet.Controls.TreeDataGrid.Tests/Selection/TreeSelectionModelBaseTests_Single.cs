using AwesomeAssertions;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Selection;
using Signet.Controls.TreeDataGrid.Selection;
using Avalonia.Controls.Utils;
using Signet.Controls.TreeDataGrid.Utils;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests
{
    public class TreeSelectionModelBaseTests_Single
    {
        public class Source
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Changing_Source_To_NonNull_First_Clears_Old_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 2);

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().Equal(new[] { new IndexPath(0, 2) });
                    e.DeselectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    target.Count.Should().Be(0);
                    ++raised;
                };

                target.Source = CreateData(depth: 1);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(-1);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                raised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Assign_ValueType_Collection_To_SelectionModel_Of_Object()
            {
                var target = (ISelectionModel)new SelectionModel<object>();

                target.Source = new[] { 1, 2, 3 };
            }
        }

        public class SelectedIndex
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Can_Set_SelectedIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    e.DeselectedItems.Should().BeEmpty();
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    target.Count.Should().Be(1);
                    ++raised;
                };

                target.SelectedIndex = new IndexPath(0, 2);

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
            }
            
            [AvaloniaFact(Timeout = 10000)]
            public void Can_Set_Grandchild_SelectedIndex()
            {
                var data = CreateData(depth: 3);
                var target = CreateTarget(data);
                var raised = 0;

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    e.DeselectedItems.Should().BeEmpty();
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(0, 0, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 0-0-2");
                    ++raised;
                };

                target.SelectedIndex = new IndexPath(0, 0, 2);

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-0-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Setting_SelectedIndex_Clears_Old_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 1);
                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Single().Should().Be(new IndexPath(0, 1));
                    e.DeselectedItems.Single()!.Caption.Should().Be("Node 0-1");
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    ++raised;
                };

                target.SelectedIndex = new IndexPath(0, 2);

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Set_SelectedIndex_To_Empty()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 2);
                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.DeselectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++raised;
                };

                target.SelectedIndex = default;

                raised.Should().Be(1);
                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Out_Of_Range_SelectedIndex_Clears_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 2);
                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.DeselectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++raised;
                };

                target.SelectedIndex = new IndexPath(5, 10, 250);

                raised.Should().Be(1);
                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Can_Select_Unexpanded_Item()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    e.DeselectedItems.Should().BeEmpty();
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(1, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 1-2");
                    ++raised;
                };

                target.SelectedIndex = new IndexPath(1, 2);

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(1, 2));
                target.SelectedItem!.Caption.Should().Be("Node 1-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 1-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Setting_SelectedIndex_During_CollectionChanged_Results_In_Correct_Selection()
            {
                var data = new AvaloniaList<Node>();
                var target = CreateTarget(data);
                _ = new MockBinding(target, data);

                data.Add(new Node());

                target.SelectedIndex.Should().Be(new IndexPath(0));
            }

            [AvaloniaFact(Timeout = 10000)]
            public void PropertyChanged_Is_Raised()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(0, 2);

                raised.Should().Be(1);
            }

            private class MockBinding : ICollectionChangedListener
            {
                private readonly TestTreeSelectionModel _target;

                public MockBinding(TestTreeSelectionModel target, AvaloniaList<Node> data)
                {
                    _target = target;
                    CollectionChangedEventManager.Instance.AddListener(data, this);
                }

                public void Changed(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
                {
                    _target.Select(new IndexPath(0));
                }

                public void PostChanged(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
                {
                }

                public void PreChanged(INotifyCollectionChanged sender, NotifyCollectionChangedEventArgs e)
                {
                }
            }
        }

        public class SelectedItem
        {
            [AvaloniaFact(Timeout = 10000)]
            public void PropertyChanged_Is_Raised_When_SelectedIndex_Changes()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(1);

                raised.Should().Be(1);
            }
        }

        public class SelectedIndexes
        {
            [AvaloniaFact(Timeout = 10000)]
            public void PropertyChanged_Is_Raised_When_SelectedIndex_Changes()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndexes))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(1);

                raised.Should().Be(1);
            }
        }

        public class SelectedItems
        {
            [AvaloniaFact(Timeout = 10000)]
            public void PropertyChanged_Is_Raised_When_SelectedIndex_Changes()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedItems))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(1);

                raised.Should().Be(1);
            }
        }
        
        public class Select
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Select_Sets_SelectedIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    e.DeselectedItems.Should().BeEmpty();
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    ++raised;
                };

                target.Select(new IndexPath(0, 2));

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Select_Clears_Old_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.Select(new IndexPath(0, 1));

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Single().Should().Be(new IndexPath(0, 1));
                    e.DeselectedItems.Single()!.Caption.Should().Be("Node 0-1");
                    e.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                    e.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
                    ++raised;
                };

                target.Select(new IndexPath(0, 2));

                raised.Should().Be(1);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Select_With_Invalid_Index_Does_Nothing()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 2);
                target.SelectionChanged += (s, e) => ++raised;

                target.Select(new IndexPath(5, 10, 250));

                raised.Should().Be(0);
                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Single().Should().Be(new IndexPath(0, 2));
                target.SelectedItem!.Caption.Should().Be("Node 0-2");
                target.SelectedItems.Single()!.Caption.Should().Be("Node 0-2");
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Selecting_Already_Selected_Item_Doesnt_Raise_SelectionChanged()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 2);
                target.SelectionChanged += (s, e) => ++raised;

                target.Select(new IndexPath(0, 2));

                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Selecting_Item_Twice_Results_In_Correct_Count()
            {
                var target = CreateTarget();

                using (target.BatchUpdate())
                {
                    target.SelectedIndex = new IndexPath(1);
                    target.Select(new IndexPath(1));
                }

                target.Count.Should().Be(1);
            }
        }

        public class Deselect
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Deselect_Clears_Current_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 1);

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().Equal(new[] { new IndexPath(0, 1) });
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++raised;
                };

                target.Deselect(new IndexPath(0, 1));

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                raised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Deselect_Does_Nothing_For_Nonselected_Item()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 1);
                target.SelectionChanged += (s, e) => ++raised;
                target.Deselect(new IndexPath(0, 0));

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0, 1) });
                target.SelectedItem!.Caption.Should().Be("Node 0-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                raised.Should().Be(0);
            }
        }

        public class Clear
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Clear_Raises_SelectionChanged()
            {
                var target = CreateTarget();
                var raised = 0;

                target.Select(new IndexPath(0, 1));

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().Equal(new[] { new IndexPath(0, 1) });
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++raised;
                };

                target.Clear();

                raised.Should().Be(1);
            }
        }

        public class AnchorIndex
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Setting_SelectedIndex_Sets_AnchorIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.AnchorIndex))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(0, 1);

                target.AnchorIndex.Should().Be(new IndexPath(0, 1));
                raised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Setting_SelectedIndex_To_Empty_Doesnt_Clear_AnchorIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 1);

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.AnchorIndex))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = default;

                target.AnchorIndex.Should().Be(new IndexPath(0, 1));
                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Select_Sets_AnchorIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.AnchorIndex))
                    {
                        ++raised;
                    }
                };

                target.Select(new IndexPath(0, 1));

                target.AnchorIndex.Should().Be(new IndexPath(0, 1));
                raised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Deselect_Doesnt_Clear_AnchorIndex()
            {
                var target = CreateTarget();
                var raised = 0;

                target.Select(new IndexPath(0, 1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.AnchorIndex))
                    {
                        ++raised;
                    }
                };

                target.Deselect(new IndexPath(0, 1));

                target.AnchorIndex.Should().Be(new IndexPath(0, 1));
                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Raises_PropertyChanged()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.AnchorIndex))
                    {
                        ++raised;
                    }
                };

                target.SelectedIndex = new IndexPath(0, 1);

                raised.Should().Be(1);
            }
        }

        public class SingleSelect
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Converting_To_Multiple_Selection_Preserves_Selection()
            {
                var target = CreateTarget();
                var raised = 0;

                target.SelectedIndex = new IndexPath(0, 1);

                target.SelectionChanged += (s, e) => ++raised;

                target.SingleSelect = false;

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0, 1) });
                target.SelectedItem!.Caption.Should().Be("Node 0-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Raises_PropertyChanged()
            {
                var target = CreateTarget();
                var raised = 0;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SingleSelect))
                    {
                        ++raised;
                    }
                };

                target.SingleSelect = false;

                raised.Should().Be(1);
            }
        }

        public class CollectionChanges
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Adding_Root_Item_Before_Selected_Root_Item_Updates_Indexes()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.SelectedIndex = new IndexPath(1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(default);
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(1);
                    ++indexesChangedRaised;
                };

                data.Insert(0, new Node { Caption = "new" });

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(2));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(2) });
                target.SelectedItem!.Caption.Should().Be("Node 1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                target.AnchorIndex.Should().Be(new IndexPath(2));
                indexesChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Adding_Child_Item_Before_Selected_Child_Item_Updates_Indexes()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.SelectedIndex = new IndexPath(0, 1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(new IndexPath(0));
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(1);
                    ++indexesChangedRaised;
                };

                data[0].Children!.Insert(0, new Node { Caption = "new" });

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 2));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0, 2) });
                target.SelectedItem!.Caption.Should().Be("Node 0-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                target.AnchorIndex.Should().Be(new IndexPath(0, 2));
                indexesChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Adding_Root_Item_Before_Selected_Child_Item_Updates_Indexes()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.SelectedIndex = new IndexPath(0, 1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(default);
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(1);
                    ++indexesChangedRaised;
                };

                data.Insert(0, new Node { Caption = "new" });

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(1, 1) });
                target.SelectedItem!.Caption.Should().Be("Node 0-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                target.AnchorIndex.Should().Be(new IndexPath(1, 1));
                indexesChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Adding_Root_Item_Before_Selected_Grandchild_Item_Updates_Indexes()
            {
                var data = CreateData(depth: 3);
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.SelectedIndex = new IndexPath(0, 0, 1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(default);
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(1);
                    ++indexesChangedRaised;
                };

                data.Insert(0, new Node { Caption = "new" });

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 0, 1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(1, 0, 1) });
                target.SelectedItem!.Caption.Should().Be("Node 0-0-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-0-1" });
                target.AnchorIndex.Should().Be(new IndexPath(1, 0, 1));
                indexesChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Adding_Root_Item_After_Selected_Root_Item_Doesnt_Raise_Events()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var raised = 0;

                target.SelectedIndex = new IndexPath(1);

                target.PropertyChanged += (s, e) => ++raised;
                target.SelectionChanged += (s, e) => ++raised;
                target.IndexesChanged += (s, e) => ++raised;

                data.Insert(2, new Node { Caption = "new" });

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(1) });
                target.SelectedItem!.Caption.Should().Be("Node 1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                target.AnchorIndex.Should().Be(new IndexPath(1));
                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Root_Selected_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.Select(new IndexPath(1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++selectionChangedRaised;
                };

                data.RemoveAt(1);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                target.AnchorIndex.Should().Be(default);
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Child_Selected_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.Select(new IndexPath(0, 1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++selectionChangedRaised;
                };

                data[0].Children!.RemoveAt(1);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                target.AnchorIndex.Should().Be(default);
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Parent_Of_Selected_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.Select(new IndexPath(0, 1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 0-1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++selectionChangedRaised;
                };

                data.RemoveAt(0);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                target.AnchorIndex.Should().Be(default);
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Root_Item_Before_Selected_Root_Item_Updates_Indexes()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedraised = 0;

                target.SelectedIndex = new IndexPath(1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.IndexesChanged += (s, e) =>
                {
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(-1);
                    ++indexesChangedraised;
                };

                data.RemoveAt(0);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0) });
                target.SelectedItem!.Caption.Should().Be("Node 1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                target.AnchorIndex.Should().Be(new IndexPath(0));
                indexesChangedraised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Root_Item_Before_Selected_Child_Item_Updates_Indexes()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedraised = 0;

                target.SelectedIndex = new IndexPath(1, 1);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.IndexesChanged += (s, e) =>
                {
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(-1);
                    ++indexesChangedraised;
                };

                data.RemoveAt(0);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(0, 1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0, 1) });
                target.SelectedItem!.Caption.Should().Be("Node 1-1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1-1" });
                target.AnchorIndex.Should().Be(new IndexPath(0, 1));
                indexesChangedraised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Child_Item_Before_Selected_Grandhild_Item_Updates_Indexes()
            {
                var data = CreateData(depth: 3);
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var indexesChangedraised = 0;

                target.SelectedIndex = new IndexPath(1, 1, 2);

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                target.IndexesChanged += (s, e) =>
                {
                    e.StartIndex.Should().Be(0);
                    e.Delta.Should().Be(-1);
                    ++indexesChangedraised;
                };

                data[1].Children!.RemoveAt(0);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 0, 2));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(1, 0, 2) });
                target.SelectedItem!.Caption.Should().Be("Node 1-1-2");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1-1-2" });
                target.AnchorIndex.Should().Be(new IndexPath(1, 0, 2));
                indexesChangedraised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Removing_Root_Item_After_Selected_Root_Item_Doesnt_Raise_Events()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var raised = 0;

                target.SelectedIndex = new IndexPath(1);

                target.PropertyChanged += (s, e) => ++raised;
                target.SelectionChanged += (s, e) => ++raised;
                target.IndexesChanged += (s, e) => ++raised;

                data.RemoveAt(2);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(1) });
                target.SelectedItem!.Caption.Should().Be("Node 1");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                target.AnchorIndex.Should().Be(new IndexPath(1));
                raised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Replacing_Selected_Root_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++selectionChangedRaised;
                };

                data[1] = new Node { Caption = "new" };

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Replacing_Selected_Child_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1, 1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    e.DeselectedIndexes.Should().BeEmpty();
                    (e.DeselectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "Node 1-1" });
                    e.SelectedIndexes.Should().BeEmpty();
                    e.SelectedItems.Should().BeEmpty();
                    ++selectionChangedRaised;
                };

                data[1].Children![1] = new Node { Caption = "new" };

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Moving_Selected_Root_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var indexesChangedRaised = 0;
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1));

                target.IndexesChanged += (s, e) =>
                {
                    ++indexesChangedRaised;
                };

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    ++selectionChangedRaised;
                };

                data.Move(1, 3);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                indexesChangedRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Moving_Selected_Child_Item_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var indexesChangedRaised = 0;
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1, 1));

                target.IndexesChanged += (s, e) =>
                {
                    ++indexesChangedRaised;
                };

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    ++selectionChangedRaised;
                };

                data[1].Children!.Move(1, 2);

                target.Count.Should().Be(0);
                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                indexesChangedRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(1);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Moving_Unselected_Child_Item_From_Before_Selected_Item_To_After_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var indexesChangedRaised = 0;
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1, 1));

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(new IndexPath(1));
                    e.StartIndex.Should().Be(0);
                    e.EndIndex.Should().Be(2);
                    e.Delta.Should().Be(-1);
                    ++indexesChangedRaised;
                };

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    ++selectionChangedRaised;
                };

                data[1].Children!.Move(0, 2);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 0));
                target.SelectedIndexes.Should().Equal([new(1, 0)]);
                (target.SelectedItem?.Caption).Should().Be("Node 1-1");
                (target.SelectedItems.Select(x => x?.Caption)).Should().Equal(["Node 1-1"]);
                indexesChangedRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(0);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Moving_Unselected_Child_Item_From_After_Selected_Item_To_Before_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var indexesChangedRaised = 0;
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;
                var selectedItemRaised = 0;

                target.Select(new IndexPath(1, 1));

                target.IndexesChanged += (s, e) =>
                {
                    e.ParentIndex.Should().Be(new IndexPath(1));
                    e.StartIndex.Should().Be(0);
                    e.EndIndex.Should().Be(2);
                    e.Delta.Should().Be(1);
                    ++indexesChangedRaised;
                };

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }

                    if (e.PropertyName == nameof(target.SelectedItem))
                    {
                        ++selectedItemRaised;
                    }
                };

                target.SelectionChanged += (s, e) =>
                {
                    ++selectionChangedRaised;
                };

                data[1].Children!.Move(2, 0);

                target.Count.Should().Be(1);
                target.SelectedIndex.Should().Be(new IndexPath(1, 2));
                target.SelectedIndexes.Should().Equal([new(1, 2)]);
                (target.SelectedItem?.Caption).Should().Be("Node 1-1");
                (target.SelectedItems.Select(x => x?.Caption)).Should().Equal(["Node 1-1"]);
                indexesChangedRaised.Should().Be(1);
                selectionChangedRaised.Should().Be(0);
                selectedIndexRaised.Should().Be(1);
                selectedItemRaised.Should().Be(0);
            }


            [AvaloniaFact(Timeout = 10000)]
            public void Resetting_Root_Updates_State()
            {
                var data = CreateData();
                var target = CreateTarget(data);
                var selectionChangedRaised = 0;
                var selectedIndexRaised = 0;

                target.Select(new IndexPath(1));

                target.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(target.SelectedIndex))
                    {
                        ++selectedIndexRaised;
                    }
                };

                target.SelectionChanged += (s, e) => ++selectionChangedRaised;

                data.Clear();

                target.SelectedIndex.Should().Be(default);
                target.SelectedIndexes.Should().BeEmpty();
                target.SelectedItem.Should().BeNull();
                target.SelectedItems.Should().BeEmpty();
                target.AnchorIndex.Should().Be(default);
                selectionChangedRaised.Should().Be(0);
                selectedIndexRaised.Should().Be(1);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Handles_Selection_Made_In_CollectionChanged()
            {
                // Tests the following scenario:
                //
                // - Items changes from empty to having 1 item
                // - ViewModel auto-selects item 0 in CollectionChanged
                // - SelectionModel receives CollectionChanged
                // - And so adjusts the selected item from 0 to 1, which is past the end of the items.
                //
                // There's not much we can do about this situation because the order in which
                // CollectionChanged handlers are called can't be known (the problem also exists with
                // WPF). The best we can do is not select an invalid index.
                var data = new AvaloniaList<Node>();
                var target = CreateTarget(data);

                data.CollectionChanged += (s, e) =>
                {
                    target.Select(new IndexPath(0));
                };

                data.Add(new Node { Caption = "foo" });

                target.SelectedIndex.Should().Be(new IndexPath(0));
                target.SelectedIndexes.Should().Equal(new[] { new IndexPath(0) });
                target.SelectedItem!.Caption.Should().Be("foo");
                (target.SelectedItems.Select(x => x!.Caption)).Should().Equal(new[] { "foo" });
                target.AnchorIndex.Should().Be(new IndexPath(0));
            }
        }

        private static AvaloniaList<Node> CreateNodes(IndexPath parentId, int depth = 2)
        {
            var result = new AvaloniaList<Node>();

            for (var i = 0; i < 5; ++i)
            {
                var id = parentId.Append(i);

                var node = new Node
                {
                    Id = id,
                    Caption = "Node " + string.Join("-", id.ToArray()),
                    TargetDepth = depth,
                };

                result.Add(node);
            }

            return result;
        }

        private static AvaloniaList<Node> CreateData(int depth = 2)
        {
            return CreateNodes(default, depth);
        }

        private static TestTreeSelectionModel CreateTarget(AvaloniaList<Node>? data = null)
        {
            return new TestTreeSelectionModel(data ?? CreateData());
        }

        private class Node
        {
            public IndexPath Id { get; set; }
            public string? Caption { get; set; }
            public AvaloniaList<Node>? Children { get; set; }
            public int TargetDepth { get; set; }
        }

        private class TestTreeSelectionModel : TreeSelectionModelBase<Node>
        {
            public TestTreeSelectionModel(AvaloniaList<Node> data)
                : base(data)
            {
            }

            public new IEnumerable? Source
            {
                get => base.Source;
                set => base.Source = value;
            }

            protected internal override IEnumerable<Node>? GetChildren(Node node)
            {
                if (node.Children is null && node.Id.Count < node.TargetDepth)
                    node.Children = CreateNodes(node.Id, node.TargetDepth);
                return node.Children;
            }
        }
    }
}
