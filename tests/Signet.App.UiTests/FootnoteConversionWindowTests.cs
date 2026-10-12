using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.UiTests;

/// <summary>Render tests of the "Convert Footnotes of Other Origin" dialog (<see cref="FootnoteConversionWindow"/>).</summary>
public sealed class FootnoteConversionWindowTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static FootnoteConversionWindow Show(Book book)
    {
        FootnoteConversionViewModel vm = new(
            FootnoteEditor.PlanConversion(book), null, (_, _) => { }, _ => Task.FromResult<FootnoteConversionOutcome?>(null));
        FootnoteConversionWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return window;
    }

    [AvaloniaFact]
    public void The_rows_show_the_footnotes_and_the_notes_file_appears_only_for_global_ones()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource notes = book.CreateEmptyHtmlFile();
        chapter.SetText(Xhtml(
            "<p>A<a href=\"#a\" id=\"a-back\">1</a> B<a href=\"" + notes.Filename + "#b\" id=\"b-back\">*</a>.</p>\n"
            + "<div id=\"a\"><p><a href=\"#a-back\">1</a> Local note.</p></div>"));
        notes.SetText(Xhtml("<div id=\"b\"><p><a href=\"" + chapter.Filename + "#b-back\">*</a> Global note.</p></div>"));
        FootnoteConversionWindow window = Show(book);
        FootnoteConversionViewModel vm = (FootnoteConversionViewModel)window.DataContext!;

        ListBox items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ItemList");
        items.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "NoteLine").Select(t => t.Text)
            .Should().Equal(vm.Items.Select(i => i.NoteText));
        ListBox files = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "FileList");
        files.IsEffectivelyVisible.Should().BeFalse("only the local footnote is checked");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConvertButton").IsEffectivelyEnabled.Should().BeTrue();

        vm.Items[1].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        files.IsEffectivelyVisible.Should().BeTrue();
        files.SelectedItem.Should().BeSameAs(vm.Files[0], "the only, dedicated notes file is chosen automatically");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConvertButton").IsEffectivelyEnabled.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void Without_footnotes_of_other_origin_the_dialog_says_so()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        FootnoteConversionWindow window = Show(book);

        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("FootnoteConversionWindow_None") && t.IsEffectivelyVisible);
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConvertButton").IsEffectivelyEnabled.Should().BeFalse();
        window.Close();
    }
}
