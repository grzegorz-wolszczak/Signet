using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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

/// <summary>The "Unify Letters of Shared Footnotes" dialog: its tree, the style and "Unify".</summary>
public sealed class FootnoteLettersWindowTests
{
    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    [AvaloniaFact]
    public void The_tree_shows_the_notes_and_unify_rewrites_them_and_closes_the_dialog()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
            + "<head><title>t</title></head>\n<body>\n<p>One.</p>\n<p>Two.</p>\n</body>\n</html>");
        FootnoteOptions upper = FootnoteOptions.Default with { LetterStyle = FootnoteLetterStyle.UpperLatin };
        FootnoteEditor.Add(book, html, After(html, "One"), "Note.", FootnotePlacement.Local, out Footnote? footnote, upper);
        FootnoteEditor.AddReference(book, html, After(html, "Two"), footnote!, out _, upper).Should().Be(FootnoteError.None);
        FootnoteLettersViewModel vm = new(
            FootnoteLetterStyle.LowerLatin, s => FootnoteEditor.PlanLetters(book, s), (_, _) => { },
            (items, style) => FootnoteEditor.UnifyLetters(book, items, style) > 0);
        Window owner = new();
        owner.Show();

        Task shown = FootnoteLettersWindow.ShowAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteLettersWindow window = owner.OwnedWindows.OfType<FootnoteLettersWindow>().Single();
        window.CaptureRenderedFrame();

        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "Tree");
        tree.Source!.Rows.Count.Should().Be(2, "the file and its note, expanded");
        Button unify = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UnifyButton");
        unify.IsEnabled.Should().BeTrue();
        unify.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        shown.IsCompleted.Should().BeTrue("the dialog closes after unifying");
        html.GetText().Should().Contain("[1a]</a>").And.Contain("[1b]</a>");
        owner.Close();
    }
}
