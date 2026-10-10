using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="EpubUpgrader"/> — converting an EPUB 2 book to EPUB 3.</summary>
public sealed class EpubUpgraderTests
{
    [Fact]
    public void An_epub3_book_is_left_alone()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        string opf = book.GetOpf().GetText();

        EpubUpgrader.UpgradeToEpub3(book).Should().BeNull();

        book.GetOpf().GetText().Should().Be(opf);
    }

    [Fact]
    public void The_book_becomes_epub3_and_keeps_the_ncx_and_the_guide()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource section = book.GetHtmlResources()[0];
        book.GetOpf().AddGuideSemanticCode(section, "text");

        EpubUpgradeResult? result = EpubUpgrader.UpgradeToEpub3(book);

        result.Should().NotBeNull();
        book.IsEpub3.Should().BeTrue();
        book.Modified.Should().BeTrue();
        book.GetOpf().GetOpfDocument().Version.Should().Be("3.0");
        book.GetAllResources().Should().OnlyContain(r => r.EpubVersion == "3.0");
        book.GetNcx().Should().NotBeNull("EPUB 2 readers keep their table of contents");
        book.GetOpf().GetOpfDocument().Guide.Should().ContainSingle(g => g.Type == "text");
        book.GetOpf().GetOpfDocument().Metadata
            .Should().Contain(m => m.Attributes.Value("property") == "dcterms:modified");
    }

    [Fact]
    public void The_nav_gets_the_ncx_entries_and_the_guide_landmarks()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource section = book.GetHtmlResources()[0];
        book.GetOpf().AddGuideSemanticCode(section, "text");
        book.GetOpf().AddGuideSemanticCode(section, "other.afterword", toggle: false, tgtId: "after");
        int ncxEntries = book.GetNcx()!.GetNcxDocument().NavMap.Count;

        EpubUpgradeResult result = EpubUpgrader.UpgradeToEpub3(book)!;

        book.GetNavResource().Should().BeSameAs(result.Nav);
        NavProcessor nav = new(result.Nav);
        nav.GetToc().Should().HaveCount(ncxEntries).And.OnlyContain(e => e.Href.EndsWith(section.Filename));
        nav.GetLandmarks().Select(l => (l.EpubType, l.Href)).Should().Equal(
            ("bodymatter", section.Filename), ("afterword", section.Filename + "#after"));
        result.TocEntries.Should().Be(ncxEntries);
        result.Landmarks.Should().Be(2);
    }

    [Fact]
    public void The_nav_takes_the_language_of_the_book()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        OpfDocument document = book.GetOpf().GetOpfDocument();
        document.Metadata.First(m => m.Name == "dc:language").Content = "pl";
        book.GetOpf().SetOpfDocument(document);

        EpubUpgradeResult result = EpubUpgrader.UpgradeToEpub3(book)!;

        result.Nav.GetText().Should().Contain("lang=\"pl\"");
    }

    [Fact]
    public void Html_files_get_the_html5_doctype()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource section = book.GetHtmlResources()[0];
        section.GetText().Should().Contain("XHTML 1.1");

        EpubUpgradeResult result = EpubUpgrader.UpgradeToEpub3(book)!;

        section.GetText().Should().Contain("<!DOCTYPE html>").And.NotContain("XHTML 1.1");
        result.Doctypes.Should().Be(1);
    }

    [Fact]
    public void A_doctype_with_an_internal_subset_is_kept()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource section = book.GetHtmlResources()[0];
        string doctype = "<!DOCTYPE html [ <!ENTITY nbsp \"&#160;\"> ]>";
        section.SetText("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + doctype
            + "\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title></title></head><body><p>a</p></body></html>");

        EpubUpgrader.ReplaceDoctype(section).Should().BeFalse();

        section.GetText().Should().Contain(doctype);
    }

    [Fact]
    public void Epub2_metadata_attributes_become_epub3_metadata()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        OpfDocument document = book.GetOpf().GetOpfDocument();
        MetaEntry creator = new() { Name = "dc:creator", Content = "Jane Doe" };
        creator.Attributes.Set("opf:role", "aut");
        creator.Attributes.Set("opf:file-as", "Doe, Jane");
        MetaEntry isbn = new() { Name = "dc:identifier", Content = "9780000000002" };
        isbn.Attributes.Set("opf:scheme", "ISBN");
        MetaEntry published = new() { Name = "dc:date", Content = "2020-01-01" };
        published.Attributes.Set("opf:event", "publication");
        MetaEntry modified = new() { Name = "dc:date", Content = "2021-01-01" };
        modified.Attributes.Set("opf:event", "modification");
        MetaEntry fixedLayout = new() { Name = "meta" };
        fixedLayout.Attributes.Set("name", "fixed-layout");
        fixedLayout.Attributes.Set("content", "true");
        document.Metadata.AddRange(new[] { creator, isbn, modified, published, fixedLayout });
        book.GetOpf().SetOpfDocument(document);

        EpubUpgrader.UpgradeToEpub3(book);

        MetaEntry[] metadata = book.GetOpf().GetOpfDocument().Metadata.ToArray();
        MetaEntry upgradedCreator = metadata.Single(m => m.Name == "dc:creator");
        string id = upgradedCreator.Attributes.Value("id");
        id.Should().NotBeEmpty();
        upgradedCreator.Attributes.Keys.Should().Equal("id");
        metadata.Should().ContainSingle(m => m.Attributes.Value("refines") == "#" + id
            && m.Attributes.Value("property") == "role" && m.Attributes.Value("scheme") == "marc:relators" && m.Content == "aut");
        metadata.Should().ContainSingle(m => m.Attributes.Value("refines") == "#" + id
            && m.Attributes.Value("property") == "file-as" && m.Content == "Doe, Jane");
        metadata.Should().ContainSingle(m => m.Name == "dc:identifier" && m.Content == "urn:isbn:9780000000002")
            .Which.Attributes.Contains("opf:scheme").Should().BeFalse();
        metadata.Where(m => m.Name == "dc:date").Select(m => m.Content).Should().Equal("2020-01-01");
        metadata.Should().ContainSingle(m => m.Attributes.Value("property") == "rendition:layout")
            .Which.Content.Should().Be("pre-paginated");
        metadata.Should().NotContain(m => m.Name.StartsWith("dc:", StringComparison.Ordinal)
            && m.Attributes.Keys.Any(k => k.StartsWith("opf:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Html_entries_get_their_manifest_properties()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource section = book.GetHtmlResources()[0];
        section.SetText(section.GetText().Replace("<p>", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg><p>"));

        EpubUpgradeResult result = EpubUpgrader.UpgradeToEpub3(book)!;

        book.GetOpf().GetManifestPropertiesForResource(section).Should().Be("svg");
        book.GetOpf().GetManifestPropertiesForResource(result.Nav).Should().Be("nav");
    }
}
