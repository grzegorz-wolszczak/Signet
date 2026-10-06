using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
using Signet.Core.Toc;

namespace Signet.App.UiTests;

/// <summary>The "Table Of Contents" panel: the TOC tree (Title, Target), expanded, and navigation to an entry.</summary>
public sealed class TableOfContentsViewTests
{
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    // A book whose TOC is Alpha > Alpha One, Beta; the panel shown on it.
    private static (Window Window, TableOfContentsViewModel Vm, TreeDataGrid Tree, Book Book) Show()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>\n</body>" + text[end..]);
        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);
        TableOfContentsViewModel vm = new();
        vm.SetBook(book);
        Window window = new() { Width = 600, Height = 400, Content = new TableOfContentsView { DataContext = vm } };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single(), book);
    }

    private static Point CellCenter(TreeDataGrid tree, Window window, int column, int row)
    {
        Control cell = tree.TryGetCell(column, row)!;
        return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void The_tree_shows_every_entry_expanded_with_its_target()
    {
        (Window window, TableOfContentsViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
                Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnTarget"));
            tree.Rows!.Count.Should().Be(3, "the nested entry is shown expanded");
            TocEntryViewModel alphaOne = vm.Nodes[0].Children[0];
            alphaOne.TargetDisplay.Should().StartWith(alphaOne.Entry.TargetBookPath).And.Contain("#");
            tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
                .Should().Contain("Alpha").And.Contain("Alpha One").And.Contain("Beta").And.Contain(alphaOne.TargetDisplay);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Double_click_and_Enter_navigate_to_the_entry()
    {
        (Window window, TableOfContentsViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            TocDisplayEntry? activated = null;
            vm.EntryActivated += (_, entry) => activated = entry;
            Point beta = CellCenter(tree, window, 1, 2);

            window.MouseDown(beta, MouseButton.Left);
            window.MouseUp(beta, MouseButton.Left);
            window.MouseDown(beta, MouseButton.Left);
            window.MouseUp(beta, MouseButton.Left);
            Settle(window);

            activated.Should().BeSameAs(vm.Nodes[1].Entry);

            activated = null;
            tree.RowSelection!.SelectedIndex = new IndexPath(0);
            ((Control)tree.TryGetRow(0)!).Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);

            activated.Should().BeSameAs(vm.Nodes[0].Entry);
            window.Close();
        }
    }
}
