using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("noteCell")).Select(t => t.Text)
            .Should().Equal(new[] { string.Empty }.Concat(vm.Items.Select(i => i.Note)), "the file row has no note");
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

    /// <summary>
    /// A double click in the reference column opens the reference, in the note column the note; the selected row shows
    /// its places in the details, and the filter leaves the rows that need a look.
    /// </summary>
    [AvaloniaFact]
    public void A_double_click_in_a_column_opens_its_place_and_the_details_follow_the_selection()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav()[0];
        chapter.SetText(Xhtml(
            "<p>A<a href=\"#a\" id=\"a-back\">1</a> B<a href=\"#b\" id=\"b-back\">*</a>.</p>\n"
            + "<div id=\"a\"><p><a href=\"#a-back\">1</a> Local note.</p></div>\n"
            + "<div id=\"b\"><p><a href=\"#b-back\">*</a> Starred note.</p></div>"));
        List<(string Path, int Offset)> went = new();
        FootnoteConversionViewModel vm = new(
            FootnoteEditor.PlanConversion(book), null, (path, offset) => went.Add((path, offset)),
            _ => Task.FromResult<FootnoteConversionOutcome?>(null));
        FootnoteConversionWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        string text = chapter.GetText();

        DoubleClick(window, Cell(window, "referenceCell", vm.Items[0].Reference));
        DoubleClick(window, Cell(window, "noteCell", vm.Items[0].Note));

        went.Should().Equal(
            (chapter.BookPath, text.IndexOf("<a href=\"#a\"", StringComparison.Ordinal)),
            (chapter.BookPath, text.IndexOf("<div id=\"a\"", StringComparison.Ordinal)));
        vm.Selected.Should().BeSameAs(vm.Items[0], "the click selected the row");
        window.FindControl<ItemsControl>("PlaceList")!.ItemsSource.Should().BeSameAs(vm.Items[0].Places);

        window.FindControl<CheckBox>("OnlyWarningsBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("noteCell")).Select(t => t.Text)
            .Should().Equal(string.Empty, vm.Items[1].Note);
        window.DetailsHeight = 20;
        window.DetailsHeight.Should().Be(60, "not below the minimum");
        window.Close();
    }

    private static TextBlock Cell(Window window, string kind, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains(kind) && t.Text == text);

    private static void DoubleClick(Window window, Control control)
    {
        Avalonia.Point at = Avalonia.VisualExtensions.TranslatePoint(
            control, new Avalonia.Point(Math.Min(20, control.Bounds.Width / 2), control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
