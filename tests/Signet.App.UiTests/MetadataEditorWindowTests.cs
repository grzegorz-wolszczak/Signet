using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>The "Metadata Editor" dialog: the metadata tree (Name, Value), in-place value editing and the selection.</summary>
public sealed class MetadataEditorWindowTests
{
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static (MetadataEditorWindow Window, MetadataEditorViewModel Vm, TreeDataGrid Tree, Book Book) Show()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        MetadataEditorViewModel vm = new(book);
        MetadataEditorWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single(), book);
    }

    [AvaloniaFact]
    public void The_tree_has_name_and_value_columns_and_a_value_is_edited_in_place()
    {
        (MetadataEditorWindow window, MetadataEditorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
                Strings.Get("ReportsWindow_Name"), Strings.Get("MetadataEditorWindow_ColumnValue"));
            tree.CanUserSortColumns.Should().BeFalse();
            MetadataNodeViewModel first = vm.Nodes[0];
            TextBox editor = tree.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, first));
            editor.Text.Should().Be(first.Content);

            editor.Text = "Changed value";

            first.Content.Should().Be("Changed value");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void An_added_element_is_selected_and_moving_it_keeps_it_selected()
    {
        (MetadataEditorWindow window, MetadataEditorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            tree.RowSelection!.SelectedIndex = new IndexPath(0);
            vm.SelectedNode.Should().BeSameAs(vm.Nodes[0], "a row selected in the tree selects the node");

            MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");
            vm.SelectedNode = creator;
            Settle(window);
            tree.RowSelection.SelectedItem.Should().BeSameAs(creator);

            int index = vm.Nodes.IndexOf(creator);
            vm.MoveUpCommand.Execute(null);
            Settle(window);

            vm.Nodes.IndexOf(creator).Should().Be(index - 1);
            tree.RowSelection.SelectedItem.Should().BeSameAs(creator, "the moved element stays selected");
            vm.SelectedNode.Should().BeSameAs(creator);
            window.Close();
        }
    }
}
