using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Tests of the editable table of contents model: <see cref="TocEditModel"/> +
/// <see cref="NavProcessor.GetRootTocEntry"/> / <see cref="NavProcessor.GenerateNavTocFromTocEntries"/> +
/// <see cref="NcxGenerator.GenerateFromTocEntries"/> / <see cref="NcxTocEntries.GetRootTocEntry"/>.
/// </summary>
public sealed class EditTocTests
{
    private const string HeadingBody =
        "<body>\n" +
        "  <h1>Alpha</h1>\n" +
        "  <p>x</p>\n" +
        "  <h2>Alpha One</h2>\n" +
        "  <p>x</p>\n" +
        "  <h1>Beta</h1>\n" +
        "</body>";

    private static void SetBody(Book book, string body)
    {
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + body + text[end..]);
    }

    private static Book BookWithGeneratedToc(string version)
    {
        Book book = BookCreator.CreateNewBook(version);
        SetBody(book, HeadingBody);
        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);
        return book;
    }

    [Fact]
    public void GetRootTocEntry_epub3_mirrors_the_nav_hierarchy_with_absolute_targets()
    {
        using Book book = BookWithGeneratedToc("3.0");
        string chapterBookPath = book.GetHtmlResourcesExcludingNav().First().BookPath;

        TocEntry root = TocEditModel.GetRootTocEntry(book);

        root.IsRoot.Should().BeTrue();
        root.Children.Select(e => e.Text).Should().Equal("Alpha", "Beta");
        root.Children[0].Children.Should().ContainSingle().Which.Text.Should().Be("Alpha One");
        root.Children[0].Target.Should().Be(chapterBookPath);
        root.Children[0].Children[0].Target.Should().StartWith(chapterBookPath + "#");
    }

    [Fact]
    public void Save_epub3_unchanged_tree_is_a_round_trip()
    {
        using Book book = BookWithGeneratedToc("3.0");
        var before = new NavProcessor(book.GetNavResource()!).GetToc()
            .Select(e => (e.Level, e.Title, e.Href)).ToList();

        TocEditModel.Save(book, TocEditModel.GetRootTocEntry(book));

        var after = new NavProcessor(book.GetNavResource()!).GetToc()
            .Select(e => (e.Level, e.Title, e.Href)).ToList();
        after.Should().Equal(before);
    }

    [Fact]
    public void Save_epub3_promoting_a_child_persists_to_the_nav()
    {
        using Book book = BookWithGeneratedToc("3.0");

        TocEntry root = TocEditModel.GetRootTocEntry(book);
        TocEntry alpha = root.Children[0];
        TocEntry child = alpha.Children[0];
        alpha.Children.RemoveAt(0);
        root.Children.Insert(1, child); // promote „Alpha One" to top level, between Alpha and Beta

        TocEditModel.Save(book, root);

        var flat = new NavProcessor(book.GetNavResource()!).GetToc();
        flat.Select(e => e.Title).Should().Equal("Alpha", "Alpha One", "Beta");
        flat.Select(e => e.Level).Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public void Save_epub2_writes_the_tree_to_the_ncx_navmap()
    {
        using Book book = BookWithGeneratedToc("2.0");

        TocEntry root = TocEditModel.GetRootTocEntry(book);
        root.Children[0].Text = "Renamed Alpha";

        TocEditModel.Save(book, root);

        NcxDocument ncx = book.GetNcx()!.GetNcxDocument();
        ncx.NavMap.Select(p => p.Label).Should().Equal("Renamed Alpha", "Beta");
        ncx.NavMap[0].Children.Should().ContainSingle().Which.Label.Should().Be("Alpha One");
        ncx.NavMap[0].PlayOrder.Should().Be(1);
        ncx.NavMap[0].Children[0].PlayOrder.Should().Be(2);
    }

    [Fact]
    public void Save_epub2_empty_tree_writes_a_single_fallback_navpoint()
    {
        using Book book = BookWithGeneratedToc("2.0");

        TocEditModel.Save(book, new TocEntry { IsRoot = true });

        NcxDocument ncx = book.GetNcx()!.GetNcxDocument();
        ncx.NavMap.Should().ContainSingle();
        ncx.NavMap[0].Label.Should().Be("Start");
    }

    [Fact]
    public void Round_trip_through_toc_entries_is_stable_for_epub2()
    {
        using Book book = BookWithGeneratedToc("2.0");

        TocEditModel.Save(book, TocEditModel.GetRootTocEntry(book));
        var first = book.GetNcx()!.GetNcxDocument().NavMap
            .SelectMany(Flatten).Select(p => (p.Label, p.ContentSrc)).ToList();

        TocEditModel.Save(book, TocEditModel.GetRootTocEntry(book));
        var second = book.GetNcx()!.GetNcxDocument().NavMap
            .SelectMany(Flatten).Select(p => (p.Label, p.ContentSrc)).ToList();

        second.Should().Equal(first);
    }

    private static System.Collections.Generic.IEnumerable<NcxNavPoint> Flatten(NcxNavPoint point)
    {
        yield return point;
        foreach (NcxNavPoint child in point.Children)
        {
            foreach (NcxNavPoint nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
