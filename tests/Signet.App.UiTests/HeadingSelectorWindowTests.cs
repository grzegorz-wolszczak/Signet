using System;
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
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Generate Table Of Contents" dialog: the headings tree (Title, Level, In TOC) and its selection.</summary>
public sealed class HeadingSelectorWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    // Headings Alpha (h1) > Alpha One (h2), Beta (h1); the dialog shown on them.
    private (HeadingSelectorWindow Window, HeadingSelectorViewModel Vm, TreeDataGrid Tree, Book Book) Show()
    {
        Book book = _disposables.Add(BookCreator.CreateNewBook("3.0"));
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>\n</body>" + text[end..]);
        HeadingSelectorViewModel vm = new(new HeadingSelectorModel(book));
        HeadingSelectorWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single(), book);
    }

    [AvaloniaFact]
    public void The_tree_shows_the_headings_expanded_with_their_level_and_inclusion()
    {
        // Act
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            // Assert
            tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
                Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnLevel"),
                Strings.Get("HeadingSelectorWindow_ColumnInToc"));
            tree.CanUserSortColumns.Should().BeFalse();
            tree.Rows!.Count.Should().Be(3);
            tree.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).Should().Equal("Alpha", "Alpha One", "Beta");
            tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
                .Should().Equal("h1", "h2", "h1");

            HeadingNodeViewModel alphaOne = vm.Nodes[0].Children[0];
            CheckBox include = tree.GetVisualDescendants().OfType<CheckBox>().Single(c => ReferenceEquals(c.DataContext, alphaOne));
            bool before = alphaOne.Heading.IncludeInToc;

            // Act
            include.IsChecked = !before;

            // Assert
            alphaOne.Heading.IncludeInToc.Should().Be(!before, "the check box edits the heading");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_the_level_keeps_the_heading_selected_in_the_tree()
    {
        // Act
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            tree.RowSelection!.SelectedIndex = new IndexPath(1);

            // Assert
            vm.SelectedNode!.TitleText.Should().Be("Beta");

            // Act
            vm.IncreaseLevelCommand.Execute(null);
            Settle(window);

            // Assert
            vm.SelectedNode!.TitleText.Should().Be("Beta");
            vm.SelectedNode.LevelLabel.Should().Be("h2");
            tree.RowSelection.SelectedItem.Should().BeSameAs(vm.SelectedNode, "the rebuilt node is selected again");
            window.Close();
        }
    }
}
