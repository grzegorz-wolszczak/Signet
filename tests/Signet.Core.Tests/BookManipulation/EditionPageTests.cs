using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>The edition page: <see cref="EditionPage.Stamp"/>, <see cref="EditionPage.Find"/> and <see cref="EditionPage.Remove"/>.</summary>
public sealed class EditionPageTests
{
    private static readonly DateTime SavedAt = new(2026, 10, 10, 12, 34, 56, DateTimeKind.Utc);

    private const string ProgramVersion = "0.6.0-202610101234";

    private readonly Fixture _fixture = new();

    private static EditionPageSettings Settings(
        EditionPagePosition position = EditionPagePosition.Last,
        bool addToToc = true,
        params EditionPageField[] fields) =>
        EditionPageSettings.Default with { Enabled = true, Position = position, AddToToc = addToToc, Fields = fields };

    // A new book (one section + nav for EPUB 3 / NCX for EPUB 2) with two more sections.
    private static Book NewBook(string version = "3.0")
    {
        Book book = BookCreator.CreateNewBook(version);
        book.CreateEmptyHtmlFile();
        book.CreateEmptyHtmlFile();
        return book;
    }

    private static List<string> SpineFileNames(Book book) =>
        book.GetOpf().GetSpineOrderBookPaths().Select(p => p[(p.LastIndexOf('/') + 1)..]).ToList();

    private static void SetLanguage(Book book, string language)
    {
        OpfResource opf = book.GetOpf();
        List<MetaEntry> metadata = opf.GetDcMetadata().Where(m => m.Name != "dc:language").ToList();
        if (language.Length > 0)
        {
            metadata.Add(new MetaEntry { Name = "dc:language", Content = language });
        }

        opf.SetDcMetadata(metadata);
    }

    [Fact]
    public void Stamp_creates_a_marked_page_with_the_shown_fields_the_revision_and_the_utc_date()
    {
        using Book book = NewBook();
        string publisher = _fixture.Create<string>();
        string hidden = _fixture.Create<string>();

        EditionStampResult result = EditionPage.Stamp(
            book,
            Settings(fields: new[] { new EditionPageField("Edited by", publisher, true), new EditionPageField("Hidden", hidden, false) }),
            SavedAt,
            "en",
            ProgramVersion);

        result.Created.Should().BeTrue();
        result.Revision.Should().Be(1);
        result.Page.Filename.Should().Be(EditionPage.FileName);
        EditionPage.Find(book).Should().BeSameAs(result.Page);
        string text = result.Page.GetText();
        text.Should().Contain("<meta name=\"signet:edition-page\" content=\"1\" />");
        text.Should().Contain("<tr class=\"signet-edition-field\"><th>Edited by</th><td>" + publisher + "</td></tr>");
        text.Should().NotContain(hidden);
        text.Should().Contain("<th>Revision</th><td>1</td>");
        text.Should().Contain("<th>Last updated</th><td>2026-10-10 12:34 UTC</td>");
        book.GetOpf().GetNamedMeta(EditionPage.RevisionMetaName).Should().Be("1");
        text.Should().Contain("table.signet-edition { margin: 1.5em auto; border-collapse: collapse; font-family: monospace; }",
            "the table is set in a fixed-width font");
    }

    [Fact]
    public void The_program_and_version_rows_come_before_the_revision_and_can_be_hidden()
    {
        using Book book = NewBook();

        string text = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion).Page.GetText();

        int program = text.IndexOf("<tr class=\"signet-edition-program\"><th>Program</th><td>Signet</td></tr>", StringComparison.Ordinal);
        int version = text.IndexOf("<tr class=\"signet-edition-version\"><th>Version</th><td>" + ProgramVersion + "</td></tr>", StringComparison.Ordinal);
        int revision = text.IndexOf("signet-edition-revision", StringComparison.Ordinal);
        program.Should().BePositive();
        version.Should().BeGreaterThan(program);
        revision.Should().BeGreaterThan(version);

        string hidden = EditionPage.Stamp(
            book, Settings() with { ShowProgram = false, ShowVersion = false }, SavedAt, "en", ProgramVersion).Page.GetText();

        hidden.Should().NotContain("signet-edition-program").And.NotContain("signet-edition-version");
    }

    [Fact]
    public void Stamping_again_updates_the_same_page_and_raises_the_revision()
    {
        using Book book = NewBook();
        EditionStampResult first = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion);

        EditionStampResult second = EditionPage.Stamp(book, Settings(), SavedAt.AddHours(1), "en", ProgramVersion);

        second.Created.Should().BeFalse();
        second.Page.Should().BeSameAs(first.Page);
        second.Revision.Should().Be(2);
        book.GetHtmlResources().Count(EditionPage.IsEditionPage).Should().Be(1);
        second.Page.GetText().Should().Contain("<td>2026-10-10 13:34 UTC</td>");
    }

    [Fact]
    public void The_page_is_found_by_its_marker_wherever_it_is_and_whatever_it_is_called()
    {
        using Book book = NewBook();
        HtmlResource page = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion).Page;
        page.RenameTo("colophon.xhtml");

        EditionStampResult again = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion);

        again.Page.Should().BeSameAs(page);
        again.Page.Filename.Should().Be("colophon.xhtml");
    }

    [Theory]
    [InlineData(EditionPagePosition.First, 0)]
    [InlineData(EditionPagePosition.Second, 1)]
    [InlineData(EditionPagePosition.Penultimate, 3)]
    [InlineData(EditionPagePosition.Last, 4)]
    public void The_page_goes_to_the_chosen_place_in_the_spine(EditionPagePosition position, int expectedIndex)
    {
        using Book book = NewBook();

        EditionPage.Stamp(book, Settings(position), SavedAt, "en", ProgramVersion);

        // nav + 3 sections + the page
        List<string> spine = SpineFileNames(book);
        spine.IndexOf(EditionPage.FileName).Should().Be(expectedIndex);
    }

    [Fact]
    public void Changing_the_position_moves_an_existing_page()
    {
        using Book book = NewBook();
        EditionPage.Stamp(book, Settings(EditionPagePosition.Last), SavedAt, "en", ProgramVersion);

        EditionPage.Stamp(book, Settings(EditionPagePosition.First), SavedAt, "en", ProgramVersion);

        SpineFileNames(book)[0].Should().Be(EditionPage.FileName);
    }

    [Fact]
    public void Epub3_toc_entry_is_added_at_the_place_of_the_page_and_removed_when_switched_off()
    {
        using Book book = NewBook();
        TocEditModel.Save(book, new TocEntry
        {
            IsRoot = true,
            Children =
            {
                new TocEntry { Text = "One", Target = book.GetHtmlResourcesExcludingNav()[0].BookPath },
                new TocEntry { Text = "Two", Target = book.GetHtmlResourcesExcludingNav()[1].BookPath },
            },
        });

        HtmlResource page = EditionPage.Stamp(book, Settings(EditionPagePosition.Second), SavedAt, "en", ProgramVersion).Page;

        TocEditModel.GetRootTocEntry(book).Children.Select(e => e.Text).Should().Equal("One", "About this edition", "Two");
        page.GetText().Should().Contain("<h1 class=\"signet-edition-title\">About this edition</h1>");

        EditionPage.Stamp(book, Settings(EditionPagePosition.Second, addToToc: false), SavedAt, "en", ProgramVersion);

        TocEditModel.GetRootTocEntry(book).Children.Select(e => e.Text).Should().Equal("One", "Two");
        page.GetText().Should().Contain("class=\"signet-edition-title " + Headings.SignetNotInTocClass + "\"");
    }

    [Fact]
    public void Epub2_toc_entry_goes_to_the_ncx_and_keeps_its_page_list()
    {
        using Book book = NewBook("2.0");
        NcxResource ncx = book.GetNcx()!;
        NcxDocument document = ncx.GetNcxDocument();
        document.PageList.Add(new NcxPageTarget { Id = "p1", Type = "normal", Value = "1", Label = "1", ContentSrc = "Text/Section0001.xhtml" });
        ncx.SetNcxDocument(document);

        EditionPage.Stamp(book, Settings(EditionPagePosition.Last), SavedAt, "en", ProgramVersion);

        NcxDocument after = ncx.GetNcxDocument();
        after.NavMap.Last().Label.Should().Be("About this edition");
        after.NavMap.Last().ContentSrc.Should().EndWith(EditionPage.FileName);
        after.PageList.Should().ContainSingle();
    }

    [Theory]
    [InlineData("pl", "en", "O tym wydaniu", "Rewizja", "Data zapisu")]
    [InlineData("pl-PL", "en", "O tym wydaniu", "Rewizja", "Data zapisu")]
    [InlineData("en", "pl", "About this edition", "Revision", "Last updated")]
    [InlineData("", "pl", "O tym wydaniu", "Rewizja", "Data zapisu")]
    [InlineData("de", "fr", "About this edition", "Revision", "Last updated")]
    public void Default_texts_follow_the_book_language_then_the_ui_language(
        string bookLanguage, string uiLanguage, string title, string revision, string date)
    {
        using Book book = NewBook();
        SetLanguage(book, bookLanguage);

        string text = EditionPage.Stamp(book, Settings(), SavedAt, uiLanguage, ProgramVersion).Page.GetText();

        text.Should().Contain(">" + title + "</h1>");
        text.Should().Contain("<th>" + revision + "</th>");
        text.Should().Contain("<th>" + date + "</th>");
        text.Should().Contain(revision == "Rewizja" ? "<th>Wersja</th>" : "<th>Version</th>");
    }

    [Fact]
    public void A_label_edited_on_the_page_is_remembered_by_the_book()
    {
        using Book book = NewBook();
        HtmlResource page = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion).Page;
        page.SetText(page.GetText().Replace("<th>Revision</th>", "<th>Build</th>", StringComparison.Ordinal));

        EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion);

        page.GetText().Should().Contain("<th>Build</th><td>2</td>");
        book.GetOpf().GetNamedMeta(EditionPage.RevisionLabelMetaName).Should().Be("Build");

        EditionPage.Remove(book).Should().BeTrue();
        string recreated = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion).Page.GetText();
        recreated.Should().Contain("<th>Build</th><td>3</td>", "the label and the counter stay in the book");
    }

    [Fact]
    public void Switching_the_book_language_does_not_count_as_a_custom_label()
    {
        using Book book = NewBook();
        SetLanguage(book, "pl");
        EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion);
        SetLanguage(book, "en");

        string text = EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion).Page.GetText();

        text.Should().Contain("<th>Revision</th>");
        book.GetOpf().GetNamedMeta(EditionPage.RevisionLabelMetaName).Should().BeNull();
    }

    [Fact]
    public void Remove_deletes_the_page_and_its_toc_entry()
    {
        using Book book = NewBook();
        EditionPage.Stamp(book, Settings(), SavedAt, "en", ProgramVersion);

        EditionPage.Remove(book).Should().BeTrue();

        EditionPage.Find(book).Should().BeNull();
        TocEditModel.GetRootTocEntry(book).Children.Should().NotContain(e => e.Text == "About this edition");
        EditionPage.Remove(book).Should().BeFalse();
    }

    [Fact]
    public void Field_values_are_escaped()
    {
        using Book book = NewBook();

        string text = EditionPage.Stamp(book, Settings(fields: new EditionPageField("A & B", "<x> \"y\"", true)), SavedAt, "en", ProgramVersion).Page.GetText();

        text.Should().Contain("<th>A &amp; B</th><td>&lt;x&gt; &quot;y&quot;</td>");
    }
}
