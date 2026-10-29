using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Signet.Core.Localization;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Tests of <see cref="NavProcessor"/> — reading/writing nav sections, landmarks, NCX &#8596; Nav conversions.
/// </summary>
public sealed class NavProcessorTests
{
    private const string NestedNav =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n" +
        "<head><title>TOC</title><meta charset=\"utf-8\"/></head>\n<body>\n" +
        "  <nav epub:type=\"toc\" id=\"toc\">\n    <h1>Table of Contents</h1>\n    <ol>\n" +
        "      <li><a href=\"text/part1.xhtml\">Part I</a>\n        <ol>\n" +
        "          <li><a href=\"text/ch1.xhtml\">Chapter 1</a></li>\n" +
        "          <li><a href=\"text/ch2.xhtml\">Chapter 2</a></li>\n        </ol>\n      </li>\n" +
        "      <li><a href=\"text/part2.xhtml\">Part II</a></li>\n    </ol>\n  </nav>\n" +
        "  <nav epub:type=\"landmarks\" id=\"landmarks\" hidden=\"\">\n    <h1>Landmarks</h1>\n    <ol>\n" +
        "      <li><a epub:type=\"bodymatter\" href=\"text/part1.xhtml\">Start</a></li>\n    </ol>\n  </nav>\n" +
        "</body>\n</html>\n";

    private const string PageListNav =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n" +
        "<head><title>TOC</title><meta charset=\"utf-8\"/></head>\n<body>\n" +
        "  <nav epub:type=\"toc\" id=\"toc\">\n    <h1>Table of Contents</h1>\n    <ol>\n" +
        "      <li><a href=\"text/ch1.xhtml\">Chapter 1</a></li>\n    </ol>\n  </nav>\n" +
        "  <nav epub:type=\"page-list\" id=\"page-list\">\n    <h1>Page List</h1>\n    <ol>\n" +
        "      <li><a href=\"text/ch1.xhtml#pg1\">1</a></li>\n" +
        "      <li><a href=\"text/ch1.xhtml#pg2\">2</a></li>\n    </ol>\n  </nav>\n" +
        "</body>\n</html>\n";

    private static HtmlResource Nav(TempDir root, string text, string bookPath = "EPUB/nav.xhtml")
    {
        HtmlResource resource = new(root.Path, root.Combine(bookPath.Replace('/', SysPath.DirectorySeparatorChar)));
        resource.SetText(text);
        return resource;
    }

    private static HtmlResource Chapter(TempDir root, string bookPath)
    {
        string full = root.Combine(bookPath.Replace('/', SysPath.DirectorySeparatorChar));
        Directory.CreateDirectory(SysPath.GetDirectoryName(full)!);
        File.WriteAllText(full, "<html><body/></html>");
        HtmlResource resource = new(root.Path, full);
        resource.SetText("<html><body/></html>");
        return resource;
    }

    private static string ReadCorpusNav(string corpusDir) =>
        File.ReadAllText(Directory.EnumerateFiles(corpusDir, "nav.xhtml", SearchOption.AllDirectories).First());

    // --- konstruktor ---

    [Fact]
    public void Constructor_fills_empty_nav_with_template()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = new(root.Path, root.Combine("EPUB", "nav.xhtml"));

        // Act
        _ = new NavProcessor(resource);

        // Assert
        string text = resource.GetText();
        text.Should().Contain("<nav epub:type=\"toc\"").And.Contain("<nav epub:type=\"landmarks\"");
        text.Should().Contain("<!DOCTYPE html>");
    }

    [Fact]
    public void Constructor_reads_language_from_html_element()
    {
        // Arrange
        using TempDir root = new();
        string src = NestedNav.Replace("lang=\"en\" xml:lang=\"en\"", "lang=\"pl\" xml:lang=\"pl\"", System.StringComparison.Ordinal);

        // Act
        NavProcessor nav = new(Nav(root, src));

        // Assert
        nav.Language.Should().Be("pl");
    }

    // --- reading ---

    [Fact]
    public void GetToc_reads_corpus_nav()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, ReadCorpusNav(CorpusPaths.Epub3WithNcx)));

        // Act
        var toc = nav.GetToc();

        // Assert
        toc.Select(e => e.Title).Should().Equal("Chapter 1", "Chapter 2");
        toc.Select(e => e.Href).Should().Equal("text/chapter1.xhtml", "text/chapter2.xhtml");
        toc.Select(e => e.Level).Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public void GetToc_flattens_nested_list_with_levels()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));

        // Act
        var toc = nav.GetToc();

        // Assert
        toc.Select(e => (e.Title, e.Level)).Should().Equal(
            ("Part I", 1), ("Chapter 1", 2), ("Chapter 2", 2), ("Part II", 1));
    }

    [Fact]
    public void GetTocTree_builds_hierarchy()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));

        // Act
        var tree = nav.GetTocTree();

        // Assert
        tree.Select(e => e.Title).Should().Equal("Part I", "Part II");
        tree[0].Children.Select(e => e.Title).Should().Equal("Chapter 1", "Chapter 2");
        tree[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void GetLandmarks_reads_corpus_media_nav()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, ReadCorpusNav(CorpusPaths.Epub3Media)));

        // Act
        var landmarks = nav.GetLandmarks();

        // Assert
        landmarks.Should().ContainSingle();
        landmarks[0].EpubType.Should().Be("bodymatter");
        landmarks[0].Href.Should().Be("text/chapter1.xhtml");
        landmarks[0].Title.Should().Be("Start");
    }

    [Fact]
    public void GetPageList_reads_entries()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, PageListNav));

        // Act
        var pages = nav.GetPageList();

        // Assert
        pages.Select(p => (p.PageName, p.Href)).Should().Equal(
            ("1", "text/ch1.xhtml#pg1"), ("2", "text/ch1.xhtml#pg2"));
    }

    // --- writing (splice) ---

    [Fact]
    public void SetToc_round_trips_and_preserves_landmarks()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = Nav(root, NestedNav);
        NavProcessor nav = new(resource);

        // Act
        var before = nav.GetToc();
        nav.SetToc(before);
        var after = nav.GetToc();

        // Assert
        after.Select(e => (e.Title, e.Level, e.Href)).Should().Equal(before.Select(e => (e.Title, e.Level, e.Href)));
        // sekcja landmarks nietknieta
        resource.GetText().Should().Contain("<a epub:type=\"bodymatter\" href=\"text/part1.xhtml\">Start</a>");
        nav.GetLandmarks().Should().ContainSingle();
    }

    [Fact]
    public void SetToc_is_idempotent_at_text_level()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = Nav(root, NestedNav);
        NavProcessor nav = new(resource);

        // Act
        nav.SetToc(nav.GetToc());
        string once = resource.GetText();
        nav.SetToc(nav.GetToc());
        string twice = resource.GetText();

        // Assert
        twice.Should().Be(once);
    }

    [Fact]
    public void SetLandmarks_inserts_section_when_missing()
    {
        // Arrange
        using TempDir root = new();
        string tocOnly =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\">\n" +
            "<head><meta charset=\"utf-8\"/></head>\n<body>\n" +
            "  <nav epub:type=\"toc\" id=\"toc\"><ol><li><a href=\"a.xhtml\">A</a></li></ol></nav>\n" +
            "</body>\n</html>\n";
        HtmlResource resource = Nav(root, tocOnly);
        NavProcessor nav = new(resource);

        // Act
        nav.SetLandmarks(new[] { new NavLandmarkEntry { EpubType = "toc", Title = "Table of Contents", Href = "nav.xhtml" } });

        // Assert
        resource.GetText().Should().Contain("<nav epub:type=\"landmarks\"");
        nav.GetLandmarks().Should().ContainSingle().Which.EpubType.Should().Be("toc");
        nav.GetToc().Should().ContainSingle();
    }

    [Fact]
    public void SetPageList_round_trips()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, PageListNav));

        // Act
        var before = nav.GetPageList();
        nav.SetPageList(before);
        var after = nav.GetPageList();

        // Assert
        after.Select(p => (p.PageName, p.Href)).Should().Equal(before.Select(p => (p.PageName, p.Href)));
    }

    // --- landmarki ---

    [Fact]
    public void AddLandmarkCode_adds_then_toggles_off()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));
        HtmlResource chapter = Chapter(root, "EPUB/text/ch1.xhtml");

        // Act
        nav.AddLandmarkCode(chapter, "bodymatter");

        // Assert
        nav.GetLandmarkCodeForResource(chapter).Should().Be("bodymatter");
        nav.GetLandmarkNameForResource(chapter).Should().Be(CoreStrings.Get("Landmark_bodymatter_Name"));

        // Act
        nav.AddLandmarkCode(chapter, "bodymatter");

        // Assert
        nav.GetLandmarkCodeForResource(chapter).Should().BeEmpty();
    }

    [Fact]
    public void AddLandmarkCode_replaces_existing_code_for_same_target()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));
        HtmlResource chapter = Chapter(root, "EPUB/text/ch1.xhtml");

        // Act
        nav.AddLandmarkCode(chapter, "bodymatter");
        nav.AddLandmarkCode(chapter, "preface");

        // Assert
        nav.GetLandmarkCodeForResource(chapter).Should().Be("preface");
        nav.GetLandmarks().Count(e => e.Href.StartsWith("text/ch1.xhtml", System.StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void RemoveWholeFileLandmarksForResource_removes_every_whole_file_entry_and_keeps_fragments()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = Nav(root, NestedNav);
        NavProcessor nav = new(resource);
        HtmlResource chapter = Chapter(root, "EPUB/text/ch1.xhtml");

        // A book from elsewhere may name one file twice among its landmarks; the editor never writes that itself.
        nav.SetLandmarks(new[]
        {
            new NavLandmarkEntry { EpubType = "bodymatter", Title = "Start", Href = "text/ch1.xhtml" },
            new NavLandmarkEntry { EpubType = "preface", Title = "Preface", Href = "text/ch1.xhtml" },
            new NavLandmarkEntry { EpubType = "epigraph", Title = "Epigraph", Href = "text/ch1.xhtml#motto" },
            new NavLandmarkEntry { EpubType = "toc", Title = "Contents", Href = "nav.xhtml" },
        });

        // Act
        bool removed = nav.RemoveWholeFileLandmarksForResource(chapter);

        // Assert
        removed.Should().BeTrue();
        nav.GetLandmarkCodeForResource(chapter).Should().BeEmpty();
        nav.GetLandmarks().Select(e => e.Href).Should().Equal("text/ch1.xhtml#motto", "nav.xhtml");
    }

    [Fact]
    public void RemoveWholeFileLandmarksForResource_without_an_entry_leaves_the_nav_alone()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = Nav(root, NestedNav);
        NavProcessor nav = new(resource);
        HtmlResource chapter = Chapter(root, "EPUB/text/ch2.xhtml");
        string before = resource.GetText();

        // Act
        bool removed = nav.RemoveWholeFileLandmarksForResource(chapter);

        // Assert
        removed.Should().BeFalse();
        resource.GetText().Should().Be(before);
    }

    [Fact]
    public void GetAllLandmarkInfoByBookPath_splits_path_fragment_code_title()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));

        // Act
        var info = nav.GetAllLandmarkInfoByBookPath();

        // Assert
        info.Should().ContainSingle();
        info[0].BookPath.Should().Be("EPUB/text/part1.xhtml");
        info[0].Fragment.Should().BeEmpty();
        info[0].Code.Should().Be("bodymatter");
        info[0].Title.Should().Be("Start");
    }

    // --- konwersje NCX <-> Nav ---

    [Fact]
    public void ToNcx_builds_navmap_from_toc_tree_with_consistent_playorder()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor nav = new(Nav(root, NestedNav));

        // Act
        NcxDocument ncx = nav.ToNcx("EPUB/toc.ncx", "My Book", "urn:uuid:1");

        // Assert
        ncx.DocTitle.Should().Be("My Book");
        ncx.DtbUid.Should().Be("urn:uuid:1");
        ncx.NavMap.Select(np => np.Label).Should().Equal("Part I", "Part II");
        ncx.NavMap[0].Children.Select(np => np.Label).Should().Equal("Chapter 1", "Chapter 2");
        ncx.NavMap[0].ContentSrc.Should().Be("text/part1.xhtml");

        // Act
        NcxDocument round = NcxDocument.Parse(ncx.ToXml());

        // Assert
        round.NavMap[0].PlayOrder.Should().Be(1);
        round.NavMap[0].Children[0].PlayOrder.Should().Be(2);
        round.NavMap[0].Children[1].PlayOrder.Should().Be(3);
        round.NavMap[1].PlayOrder.Should().Be(4);
    }

    [Fact]
    public void ApplyNcx_writes_toc_section_from_navmap()
    {
        // Arrange
        using TempDir root = new();
        NavProcessor source = new(Nav(root, NestedNav));

        // Act
        NcxDocument ncx = source.ToNcx("EPUB/toc.ncx", "T", "u");

        // Arrange
        HtmlResource target = Nav(root, string.Empty, "EPUB/nav2.xhtml");
        NavProcessor navTarget = new(target);

        // Act
        navTarget.ApplyNcx(ncx, "EPUB/toc.ncx");

        // Assert
        navTarget.GetToc().Select(e => (e.Title, e.Level)).Should().Equal(
            ("Part I", 1), ("Chapter 1", 2), ("Chapter 2", 2), ("Part II", 1));
    }

    [Fact]
    public void Nav_to_ncx_to_nav_is_stable_for_epub3_minimal()
    {
        // Arrange
        using TempDir root = new();
        HtmlResource resource = Nav(root, ReadCorpusNav(CorpusPaths.Epub3Minimal));
        NavProcessor nav = new(resource);

        // Act
        var original = nav.GetToc();
        NcxDocument ncx = nav.ToNcx("EPUB/toc.ncx", "Minimal", "urn:uuid:1");
        nav.ApplyNcx(ncx, "EPUB/toc.ncx");

        // Assert
        nav.GetToc().Select(e => (e.Title, e.Level, e.Href))
            .Should().Equal(original.Select(e => (e.Title, e.Level, e.Href)));
    }

    [Fact]
    public void Href_conversion_rebases_between_nav_and_ncx_in_different_folders()
    {
        // Arrange
        using TempDir root = new();
        // nav w EPUB/xhtml/, ncx w EPUB/
        string src = NestedNav.Replace("href=\"text/", "href=\"../text/", System.StringComparison.Ordinal);
        NavProcessor nav = new(Nav(root, src, "EPUB/xhtml/nav.xhtml"));

        // Act
        NcxDocument ncx = nav.ToNcx("EPUB/toc.ncx", "T", "u");

        // Assert
        // z EPUB/toc.ncx do EPUB/text/part1.xhtml -> text/part1.xhtml
        ncx.NavMap[0].ContentSrc.Should().Be("text/part1.xhtml");

        // Act
        nav.ApplyNcx(ncx, "EPUB/toc.ncx");

        // Assert
        // z EPUB/xhtml/nav.xhtml z powrotem -> ../text/part1.xhtml
        nav.GetToc()[0].Href.Should().Be("../text/part1.xhtml");
    }

    [Fact]
    public void MakeHierarchy_and_Flatten_round_trip()
    {
        // Arrange
        var flat = new[]
        {
            new NavTocEntry { Level = 1, Title = "A", Href = "a" },
            new NavTocEntry { Level = 2, Title = "A1", Href = "a1" },
            new NavTocEntry { Level = 3, Title = "A1a", Href = "a1a" },
            new NavTocEntry { Level = 1, Title = "B", Href = "b" },
        };

        // Act
        var tree = NavProcessor.MakeHierarchy(flat);
        var back = NavProcessor.Flatten(tree);

        // Assert
        back.Select(e => (e.Title, e.Level)).Should().Equal(
            ("A", 1), ("A1", 2), ("A1a", 3), ("B", 1));
    }
}
