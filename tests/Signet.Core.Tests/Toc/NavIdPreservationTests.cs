using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Other files can link to ids in the nav, so they must survive the regeneration of its sections (Calibre 5a3593a1ef and 82d8dc6886):
/// the ids of the nav, its heading, its lists and its entries stay with the entries they belong to; the ids of the entries that are gone
/// stay as empty anchors before the nav (a nav may hold only a heading and a list).
/// </summary>
public sealed class NavIdPreservationTests
{
    private const string Head =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n"
        + "<head><title>TOC</title><meta charset=\"utf-8\"/></head>\n<body>\n";

    private const string Tail = "</body>\n</html>\n";

    private const string Toc =
        "<nav epub:type=\"toc\" id=\"tocnav\" class=\"mynav\"><h2 id=\"toch\" class=\"t\"><span class=\"special\">Contents</span></h2><ol id=\"tocol\">"
        + "<li id=\"lia\"><a id=\"ida\" href=\"a.xhtml\">A</a></li>"
        + "<li><a id=\"idb\" href=\"b.xhtml#top\">B</a></li>"
        + "<li><a id=\"idc\" href=\"c.xhtml\">C</a></li></ol></nav>\n";

    private const string Landmarks =
        "<nav epub:type=\"landmarks\"><ol><li id=\"lmli\"><a id=\"lma\" epub:type=\"bodymatter\" href=\"a.xhtml\">Start</a></li>"
        + "<li><a id=\"lmgone\" epub:type=\"cover\" href=\"cover.xhtml\">Cover</a></li></ol></nav>\n";

    private const string PageList =
        "<nav epub:type=\"page-list\" id=\"pgnav\"><ol><li id=\"p1li\"><a id=\"p1\" href=\"a.xhtml#p1\">1</a></li>"
        + "<li><a id=\"p2\" href=\"a.xhtml#p2\">2</a></li></ol></nav>\n";

    private static IElement Find(string text, string id)
    {
        IElement[] found = new HtmlParser().ParseDocument(text).QuerySelectorAll("[id=\"" + id + "\"]").ToArray();
        found.Should().HaveCount(1, "the id " + id + " is in the nav once");
        return found[0];
    }

    private static IElement NavOf(string text, string epubType) =>
        new HtmlParser().ParseDocument(text).QuerySelectorAll("nav").Single(n => n.GetAttribute("epub:type") == epubType);

    [Fact]
    public void The_ids_of_a_toc_stay_with_the_entries_they_belong_to()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource navResource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));
        navResource.SetText(Head + Toc + Tail);
        NavProcessor processor = new(navResource);

        // Act
        processor.SetToc(
        [
            new NavTocEntry { Level = 1, Title = "A", Href = "a.xhtml" },
            new NavTocEntry { Level = 1, Title = "Changed", Href = "b.xhtml" },
        ]);

        // Assert
        string text = navResource.GetText();
        IElement nav = Find(text, "tocnav");
        nav.LocalName.Should().Be("nav");
        nav.GetAttribute("epub:type").Should().Be("toc");
        nav.GetAttribute("class").Should().Be("mynav", "the opening tag of the nav is kept");
        IElement heading = Find(text, "toch");
        heading.LocalName.Should().Be("h2");
        heading.GetAttribute("class").Should().Be("t");
        heading.QuerySelectorAll(".special").Should().ContainSingle().Which.TextContent.Should().Be("Contents", "the heading is kept as it was");
        Find(text, "tocol").LocalName.Should().Be("ol");
        Find(text, "lia").LocalName.Should().Be("li");
        Find(text, "ida").GetAttribute("href").Should().Be("a.xhtml");
        IElement b = Find(text, "idb");
        b.GetAttribute("href").Should().Be("b.xhtml", "the fragment is gone, the entry is still the one of that file");
        b.TextContent.Should().Be("Changed");
    }

    [Fact]
    public void The_id_of_an_entry_that_is_gone_stays_as_an_empty_anchor_before_the_nav_which_keeps_to_a_heading_and_a_list()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource navResource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));
        navResource.SetText(Head + Toc + Tail);
        NavProcessor processor = new(navResource);

        // Act
        processor.SetToc([new NavTocEntry { Level = 1, Title = "A", Href = "a.xhtml" }]);

        // Assert
        string text = navResource.GetText();
        AngleSharp.Html.Dom.IHtmlDocument document = new HtmlParser().ParseDocument(text);
        IElement anchor = document.QuerySelectorAll("[id=\"idc\"]").Should().ContainSingle().Subject;
        anchor.LocalName.Should().Be("span");
        anchor.TextContent.Should().BeEmpty();
        anchor.ParentElement!.LocalName.Should().Be("body");
        anchor.NextElementSibling.Should().BeSameAs(document.QuerySelector("[id=\"tocnav\"]"));
        NavOf(text, "toc").Children.Select(c => c.LocalName).Should().Equal("h2", "ol");
        text.Should().NotContain("href=\"c.xhtml\"");
    }

    [Fact]
    public void A_second_regeneration_does_not_duplicate_the_ids_kept_as_anchors()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource navResource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));
        navResource.SetText(Head + Toc + Tail);
        NavProcessor processor = new(navResource);
        processor.SetToc([new NavTocEntry { Level = 1, Title = "A", Href = "a.xhtml" }]);

        // Act
        processor.SetToc([new NavTocEntry { Level = 1, Title = "A", Href = "a.xhtml" }, new NavTocEntry { Level = 1, Title = "C", Href = "c.xhtml" }]);

        // Assert
        string text = navResource.GetText();
        new HtmlParser().ParseDocument(text).QuerySelectorAll("[id=\"idc\"]").Should().ContainSingle();
        Find(text, "ida");
    }

    [Fact]
    public void The_ids_of_the_landmarks_and_the_page_list_survive_too()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource navResource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));
        navResource.SetText(Head + Toc + Landmarks + PageList + Tail);
        NavProcessor processor = new(navResource);

        // Act
        processor.SetLandmarks([new NavLandmarkEntry { EpubType = "bodymatter", Title = "Begin", Href = "a.xhtml" }]);
        processor.SetPageList([new NavPageListEntry { PageName = "2", Href = "a.xhtml#p2" }]);

        // Assert
        string text = navResource.GetText();
        Find(text, "lmli").LocalName.Should().Be("li");
        Find(text, "lma").TextContent.Should().Be("Begin");
        Find(text, "lmgone").LocalName.Should().Be("span", "the cover landmark is gone");
        Find(text, "pgnav").GetAttribute("epub:type").Should().Be("page-list");
        Find(text, "p2").TextContent.Should().Be("2");
        Find(text, "p1").LocalName.Should().Be("span", "the first page is gone");
        Find(text, "p1li").LocalName.Should().Be("span");
        NavOf(text, "page-list").Children.Select(c => c.LocalName).Should().Equal("h1", "ol");
    }

    [Fact]
    public void A_nav_without_an_id_or_a_role_gets_the_default_ones_and_keeps_its_other_attributes()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource navResource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));
        navResource.SetText(Head + "<nav epub:type=\"toc\" class=\"mine\"><h1>Contents</h1><ol><li><a href=\"a.xhtml\">A</a></li></ol></nav>\n" + Tail);
        NavProcessor processor = new(navResource);

        // Act
        processor.SetToc([new NavTocEntry { Level = 1, Title = "A", Href = "a.xhtml" }, new NavTocEntry { Level = 1, Title = "B", Href = "b.xhtml" }]);

        // Assert
        string text = navResource.GetText();
        NavOf(text, "toc").GetAttribute("id").Should().Be("toc");
        NavOf(text, "toc").GetAttribute("role").Should().Be("doc-toc");
        NavOf(text, "toc").GetAttribute("class").Should().Be("mine");
        processor.GetToc().Select(e => e.Href).Should().Equal("a.xhtml", "b.xhtml");
        new HtmlParser().ParseDocument(text).QuerySelectorAll("[id]").Select(e => e.Id).Should().Equal("toc");
    }
}
