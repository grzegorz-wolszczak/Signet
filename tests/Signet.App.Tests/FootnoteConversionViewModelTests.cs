using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of the "Convert Footnotes of Other Origin" dialog's view model (<see cref="FootnoteConversionViewModel"/>).</summary>
public sealed class FootnoteConversionViewModelTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    // Three local footnotes: a plain one, one with a "*" marker (a warning) and one with two references (a shared note:
    // a notice; NewViewModel marks it as not convertible).
    private static (Book Book, HtmlResource Html) BookWithLocalFootnotes() => NewBook(
        "<p>A<a href=\"#a\" id=\"a-back\">1</a> B<a href=\"#b\" id=\"b-back\">*</a> C<a href=\"#c\" id=\"c-back\">3</a>"
        + " again<a href=\"#c\">3</a>.</p>\n"
        + "<div id=\"a\"><p><a href=\"#a-back\">1</a> First <i>note</i>.</p></div>\n"
        + "<div id=\"b\"><p><a href=\"#b-back\">*</a> Starred.</p></div>\n"
        + "<div id=\"c\"><p><a href=\"#c-back\">3</a> Shared.</p></div>");

    // Two global footnotes with their notes in two files: no file is chosen automatically.
    private static (Book Book, HtmlResource First, HtmlResource Second) BookWithTwoNotesFiles()
    {
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource first = book.CreateEmptyHtmlFile();
        HtmlResource second = book.CreateEmptyHtmlFile();
        chapter.SetText(Xhtml(
            "<p>A<a href=\"" + first.Filename + "#a\" id=\"a-back\">1</a> B<a href=\"" + second.Filename + "#b\" id=\"b-back\">2</a>.</p>"));
        first.SetText(Xhtml("<h1>Notes</h1>\n<div id=\"a\"><p><a href=\"" + chapter.Filename + "#a-back\">1</a> Note a.</p></div>"));
        second.SetText(Xhtml("<h1>Notes</h1>\n<div id=\"b\"><p><a href=\"" + chapter.Filename + "#b-back\">2</a> Note b.</p></div>"));
        return (book, first, second);
    }

    // The plan of the book with its third footnote (if any) marked as not convertible, as one whose reference or note
    // is not found.
    private static FootnoteConversionViewModel NewViewModel(
        Book book, Func<ConversionRequest, Task<FootnoteConversionOutcome?>>? convert = null, string? signetFile = null)
    {
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);
        plan = plan with { Items = plan.Items.Select((item, i) => i == 2 ? item with { CanConvert = false } : item).ToList() };
        return new(plan, signetFile, (_, _) => { }, convert ?? (_ => Task.FromResult<FootnoteConversionOutcome?>(null)));
    }

    [Fact]
    public void Items_that_need_a_look_or_cannot_be_converted_start_unchecked()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource _) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);

            sut.Items.Select(i => (i.IsChecked, i.HasWarnings, i.CanConvert))
                .Should().Equal((true, false, true), (false, true, true), (false, true, false));
            sut.Items[1].Warnings.Should().Equal(Strings.Format("FootnoteConversionWarning_NonNumericMarker", "*", 2));
            sut.Items[2].Warnings.Should().Equal(Strings.Get("FootnoteConversionWindow_CannotConvert"));
            sut.Summary.Should().Be(Strings.Format("FootnoteConversionWindow_Summary", 1, 3));
            sut.NeedsGlobalFile.Should().BeFalse();
            sut.CanConvert.Should().BeTrue();
            sut.HasItems.Should().BeTrue();
        }
    }

    [Fact]
    public void Check_all_skips_the_items_that_cannot_be_converted_and_uncheck_all_clears_them()
    {
        (Book book, HtmlResource _) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);

            sut.SelectAllCommand.Execute(null);
            sut.Items.Select(i => i.IsChecked).Should().Equal(true, true, false);
            sut.Request.Items.Should().HaveCount(2);

            sut.SelectNoneCommand.Execute(null);
            sut.Items.Should().OnlyContain(i => !i.IsChecked);
            sut.CanConvert.Should().BeFalse();
            sut.ConvertCommand.CanExecute(null).Should().BeFalse();
        }
    }

    [Fact]
    public void A_row_shows_the_marker_the_new_number_the_reference_in_its_text_and_the_start_of_the_note()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionItemViewModel item = NewViewModel(book).Items[0];

            item.Header.Should().Be(Strings.Format("FootnoteConversionWindow_ItemHeader", "1", 1, Strings.Get("FootnoteConversionWindow_Local")));
            item.Title.Should().Be(Strings.Format("FootnoteConversionWindow_ItemTitle", "1", 1));
            item.Kind.Should().Be(Strings.Get("FootnoteConversionWindow_Local"));
            item.Reference.Should().StartWith("A[1]");
            item.Note.Should().Be("First note.");
        }
    }

    [Fact]
    public void The_tree_puts_the_footnotes_under_their_file()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);

            FootnoteConversionFileNode file = sut.Roots.Should().ContainSingle().Subject;
            file.File.Should().BeSameAs(html);
            file.Title.Should().Be(Strings.Format("FootnoteConversionWindow_FileTitleWarnings", html.Filename, "3", "2"));
            file.DetailTitle.Should().Be(Strings.Format("FootnoteConversionWindow_FileTitleWarnings", html.BookPath, "3", "2"));
            file.Children.Should().Equal(sut.Items);
            file.IsExpanded.Should().BeTrue();
            sut.Items.Should().OnlyContain(i => !i.HasChildren, "the other reference of a shared note is its own");
        }
    }

    [Fact]
    public void The_check_box_of_a_file_shows_and_sets_its_footnotes_that_can_be_converted()
    {
        (Book book, HtmlResource _) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);
            FootnoteConversionFileNode file = sut.Roots[0];
            System.Collections.Generic.List<string?> changed = new();
            file.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            file.Check.Should().BeNull("one of the two footnotes that can be converted is checked");
            file.Check = true;

            sut.Items.Select(i => i.IsChecked).Should().Equal(true, true, false);
            file.Check.Should().BeTrue();
            changed.Should().Contain(nameof(FootnoteConversionNode.Check));
            sut.Summary.Should().Be(Strings.Format("FootnoteConversionWindow_Summary", 2, 3));

            sut.Items[0].Check = false;
            file.Check.Should().BeNull();
            file.Check = false;
            sut.Items.Should().OnlyContain(i => !i.IsChecked);
        }
    }

    [Fact]
    public void Only_those_that_need_a_look_leaves_the_footnotes_with_warnings_and_check_all_works_on_them()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource _) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);
            sut.OnlyWarningsText.Should().Be(Strings.Format("FootnoteConversionWindow_OnlyWarnings", "2"));
            sut.Selected = sut.Items[0];

            sut.OnlyWarnings = true;

            sut.Roots.SelectMany(f => f.Children).Should().Equal(sut.Items[1], sut.Items[2]);
            sut.Selected.Should().BeNull("the tree was built again");
            sut.SelectNoneCommand.Execute(null);
            sut.Items[0].IsChecked.Should().BeTrue("the hidden footnote stays checked");
            sut.Items[1].IsChecked.Should().BeFalse();
            sut.SelectAllCommand.Execute(null);
            sut.Items.Select(i => i.IsChecked).Should().Equal(true, true, false);

            sut.OnlyWarnings = false;
            sut.Roots.SelectMany(f => f.Children).Should().Equal(sut.Items);
        }
    }

    [Fact]
    public void The_details_of_a_footnote_give_the_places_of_its_references_and_note_and_go_there()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = BookWithLocalFootnotes();
        using (book)
        {
            System.Collections.Generic.List<(string Path, int Offset)> went = new();
            FootnoteConversionViewModel sut = new(
                FootnoteEditor.PlanConversion(book), null, (path, offset) => went.Add((path, offset)),
                _ => Task.FromResult<FootnoteConversionOutcome?>(null));
            string text = html.GetText();
            int Line(int offset) => text[..offset].Count(c => c == '\n') + 1;
            int reference = text.IndexOf("<a href=\"#c\" id=", StringComparison.Ordinal);
            int other = text.IndexOf("<a href=\"#c\">", StringComparison.Ordinal);
            int note = text.IndexOf("<div id=\"c\">", StringComparison.Ordinal);
            FootnoteConversionItemViewModel item = sut.Items[2];

            item.IsChecked.Should().BeTrue("a shared note is only a notice");
            item.HasWarnings.Should().BeFalse();
            item.DetailNotices.Should().Equal(Strings.Format("FootnoteConversionNotice_SharedNote", "2", "3"));
            item.Places.Select(p => (p.Label, p.Location, p.Text)).Should().Equal(
                (Strings.Get("FootnoteConversionWindow_PlaceReference"), Strings.Format("FootnoteConversionWindow_Location", html.BookPath, Line(reference)), "A1 B* C[3] again3. 1 First note. * Starred. 3 Shared."),
                (Strings.Get("FootnoteConversionWindow_PlaceNote"), Strings.Format("FootnoteConversionWindow_Location", html.BookPath, Line(note)), "Shared."),
                (Strings.Format("FootnoteConversionWindow_PlaceOtherReference", "2"), Strings.Format("FootnoteConversionWindow_Location", html.BookPath, Line(other)), "A1 B* C3 again[3]. 1 First note. * Starred. 3 Shared."));

            item.Places[0].GoTo.Execute(null);
            item.Places[1].GoTo.Execute(null);
            item.Places[2].GoTo.Execute(null);
            item.GoToNote();
            sut.Roots[0].GoToReference();

            went.Should().Equal((html.BookPath, reference), (html.BookPath, note), (html.BookPath, other), (html.BookPath, note), (html.BookPath, -1));
        }
    }

    [Fact]
    public void Global_footnotes_wait_for_the_choice_of_the_notes_file()
    {
        (Book book, HtmlResource first, HtmlResource second) = BookWithTwoNotesFiles();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);

            sut.Items.Should().OnlyContain(i => i.IsChecked && i.IsGlobal);
            sut.Files.Select(f => f.File).Should().Equal(first, second, null);
            sut.SelectedFile.Should().BeNull("with two candidates nothing is chosen automatically");
            sut.NeedsGlobalFile.Should().BeTrue();
            sut.CanConvert.Should().BeFalse();

            sut.SelectedFile = sut.Files[1];
            sut.CanConvert.Should().BeTrue();
            sut.Request.GlobalFile.Should().BeSameAs(second);

            sut.SelectedFile = sut.Files[2];
            sut.Request.GlobalFile.Should().BeNull("Signet's notes file");

            sut.SelectNoneCommand.Execute(null);
            sut.NeedsGlobalFile.Should().BeFalse();
        }
    }

    [Fact]
    public void A_dedicated_file_with_all_the_notes_is_chosen_automatically()
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource notes = book.CreateEmptyHtmlFile();
        chapter.SetText(Xhtml("<p>A<a href=\"" + notes.Filename + "#a\" id=\"a-back\">1</a>.</p>"));
        notes.SetText(Xhtml("<h1>Notes</h1>\n<div id=\"a\"><p><a href=\"" + chapter.Filename + "#a-back\">1</a> Note a.</p></div>"));
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book, signetFile: "Notes.xhtml");

            sut.SelectedFile!.File.Should().BeSameAs(notes);
            sut.SelectedFile.Text.Should().EndWith(Strings.Get("FootnoteConversionWindow_Automatic"));
            sut.Files[^1].Text.Should().Be(Strings.Format("FootnoteConversionWindow_SignetFile", "Notes.xhtml"));
            sut.CanConvert.Should().BeTrue();
        }
    }

    [Theory]
    [AutoData]
    public async Task Convert_passes_the_request_and_shows_the_plan_and_the_problems_afterwards(bool keepClasses, string detail)
    {
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = BookWithLocalFootnotes();
        using Book empty = BookCreator.CreateNewBook("3.0");
        using (book)
        {
            ConversionRequest? asked = null;
            ConversionProblem problem = new(ConversionProblemKind.TextChanged, html.BookPath, detail);
            FootnoteConversionViewModel sut = NewViewModel(book, request =>
            {
                asked = request;
                return Task.FromResult<FootnoteConversionOutcome?>(new FootnoteConversionOutcome(
                    FootnoteEditor.PlanConversion(empty), new[] { problem }, true, null));
            });
            sut.KeepClasses = keepClasses;
            ConversionItem checkedItem = sut.Items[0].Item;

            await sut.ConvertCommand.ExecuteAsync(null);

            asked!.Items.Should().ContainSingle().Which.Should().BeSameAs(checkedItem);
            asked.KeepClasses.Should().Be(keepClasses);
            asked.GlobalFile.Should().BeNull();
            sut.HasItems.Should().BeFalse();
            sut.HasProblems.Should().BeTrue();
            sut.ProblemsTitle.Should().Be(Strings.Get("FootnoteConversionWindow_ProblemsRestored"));
            sut.Problems.Should().Equal(
                Strings.Get("FootnoteConversionProblem_TextChanged") + " — " + html.BookPath + ", " + detail);
        }
    }

    [Fact]
    public async Task A_cancelled_conversion_leaves_the_dialog_as_it_is()
    {
        (Book book, HtmlResource _) = BookWithLocalFootnotes();
        using (book)
        {
            FootnoteConversionViewModel sut = NewViewModel(book);

            await sut.ConvertCommand.ExecuteAsync(null);

            sut.Items.Should().HaveCount(3);
            sut.HasProblems.Should().BeFalse();
        }
    }
}
