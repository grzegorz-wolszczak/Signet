using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.UiTests;

/// <summary>The "Add Reference to Footnote" window: the table of notes, its selection and accepting the choice.</summary>
public sealed class FootnoteReferenceWindowTests
{
    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    [AvaloniaFact]
    public void The_table_selects_the_chosen_note_follows_the_filter_and_add_returns_the_note()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
            + "<head><title>t</title></head>\n<body>\n<p>One.</p>\n<p>Two.</p>\n<p>Three.</p>\n</body>\n</html>");
        FootnoteEditor.Add(book, html, After(html, "One"), "Apples.", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Two"), "Bananas.", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteReferenceViewModel vm = new(FootnoteEditor.ReferenceTargets(book, html, After(html, "Three")));
        Window owner = new();
        owner.Show();

        Task<Footnote?> result = FootnoteReferenceWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteReferenceWindow window = owner.OwnedWindows.OfType<FootnoteReferenceWindow>().Single();
        window.CaptureRenderedFrame();

        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "NotesGrid");
        var source = (FlatTreeDataGridSource<FootnoteReferenceRow>)grid.Source!;
        source.Rows.Count.Should().Be(2);
        source.RowSelection!.SelectedItem.Should().BeSameAs(vm.Visible[1], "the bananas are the nearest note before the caret");

        source.RowSelection.Select(new IndexPath(0));
        vm.Selected!.Text.Should().Be("Apples.");
        vm.Filter = "banana";
        Dispatcher.UIThread.RunJobs();
        source.Rows.Count.Should().Be(1);
        source.RowSelection.SelectedItem.Should().BeSameAs(vm.Visible[0]);

        Button add = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        result.IsCompleted.Should().BeTrue();
        result.Result.Should().BeSameAs(vm.Visible[0].Target.Footnote);
        owner.Close();
    }
}
