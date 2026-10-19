using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Reports;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Reports;

/// <summary>Tests for <see cref="BookReportEngine"/> (the Reports dialog).</summary>
public sealed class BookReportEngineTests
{
    private static Book LoadModifiedMinimal(TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadMedia(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }

    [Fact]
    public void GetAllFiles_ReportsEveryResourceWithSpineFlag()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // Act
        var rows = BookReportEngine.GetAllFiles(book);

        // Assert
        rows.Should().HaveCount(book.GetAllResources().Count);
        rows.Single(r => r.Name == "chapter1.xhtml").Should().Match<AllFilesRow>(r => r.InSpine && r.TypeName == "HTML");
        rows.Single(r => r.Name == "nav.xhtml").InSpine.Should().BeFalse();
        rows.Single(r => r.Name == "style.css").TypeName.Should().Be("CSS");
        rows.Should().OnlyContain(r => r.SizeBytes > 0);
    }

    [Fact]
    public void GetHtmlFiles_CountsWordsAndChecksWellFormed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // Act
        var rows = BookReportEngine.GetHtmlFiles(book);

        // Assert
        HtmlFilesRow chapter = rows.Single(r => r.Name == "chapter1.xhtml");
        chapter.WellFormed.Should().BeTrue();
        // Visible text: "Chapter 1" (the title — in <title>, not in body, but words are counted over
        // the whole source with tags stripped) + heading "Chapter 1" + "Hello, world." = 6 word tokens.
        chapter.WordCount.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void GetHtmlFiles_FlagsMalformedMarkup()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter).Replace("<p>Hello, world.</p>", "<p>Hello, world.", StringComparison.Ordinal));
        });

        // Act
        var rows = BookReportEngine.GetHtmlFiles(book);

        // Assert
        rows.Single(r => r.Name == "chapter1.xhtml").WellFormed.Should().BeFalse();
    }

    [Fact]
    public void GetCssFiles_CountsSelectors()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // Act
        var rows = BookReportEngine.GetCssFiles(book);

        // Assert
        rows.Single(r => r.Name == "style.css").SelectorCount.Should().Be(2); // body, h1
    }

    [Fact]
    public void GetHtmlClassUsage_FlagsUnmatchedAndMatchedClasses()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter)
                .Replace("<p>Hello, world.</p>", "<p class=\"lead\">Hello, world.</p>", StringComparison.Ordinal));

            string css = Path.Combine(tree, "EPUB", "styles", "style.css");
            File.WriteAllText(css, File.ReadAllText(css) + "\n.lead { font-weight: bold; }\n");
        });

        // Act
        var rows = BookReportEngine.GetHtmlClassUsage(book);

        // Assert
        rows.Should().ContainSingle(r => r.ClassName == "lead");
        HtmlClassUsageRow lead = rows.Single(r => r.ClassName == "lead");
        lead.ElementName.Should().Be("p");
        lead.IsUsed.Should().BeTrue();
        lead.CssBookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void GetHtmlClassUsage_FlagsUnusedClassAsNotMatched()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter)
                .Replace("<p>Hello, world.</p>", "<p class=\"ghost\">Hello, world.</p>", StringComparison.Ordinal));
        });

        // Act
        var rows = BookReportEngine.GetHtmlClassUsage(book);

        // Assert
        rows.Single(r => r.ClassName == "ghost").IsUsed.Should().BeFalse();
    }

    [Fact]
    public void GetHtmlClassUsage_SkipsSignetMarkerClasses()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter)
                .Replace("<h1 id=\"ch1\">", "<h1 id=\"ch1\" class=\"signet_not_in_toc\">", StringComparison.Ordinal));
        });

        // Act
        var rows = BookReportEngine.GetHtmlClassUsage(book);

        // Assert
        rows.Should().NotContain(r => r.ClassName == "signet_not_in_toc");
    }

    [Fact]
    public void GetStylesInCss_DelegatesToCssSelectorUsageAnalyzer()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // Act & Assert
        BookReportEngine.GetStylesInCss(book).Should().BeEquivalentTo(CssSelectorUsageAnalyzer.GetSelectorUsage(book));
    }

    [Fact]
    public void GetLinks_ClassifiesInternalExternalAndBrokenTargets()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter).Replace(
                "<p>Hello, world.</p>",
                "<p>Hello, world.</p>\n"
                + "<p><a href=\"#ch1\">Top</a></p>\n"
                + "<p><a href=\"#missing\">Broken fragment</a></p>\n"
                + "<p><a href=\"../text/missing.xhtml\">Broken file</a></p>\n"
                + "<p><a href=\"https://example.com\">External</a></p>",
                StringComparison.Ordinal));
        });

        // Act
        var rows = BookReportEngine.GetLinks(book);

        // Assert
        rows.Single(r => r.Text == "Top").Should().Match<LinkRow>(r => r.IsInternal && r.TargetExists == true);
        rows.Single(r => r.Text == "Broken fragment").Should().Match<LinkRow>(r => r.IsInternal && r.TargetExists == false);
        rows.Single(r => r.Text == "Broken file").Should().Match<LinkRow>(r => r.IsInternal && r.TargetExists == false);
        rows.Single(r => r.Text == "External").Should().Match<LinkRow>(r => !r.IsInternal && r.TargetExists == null);
    }

    [Fact]
    public void GetCharacterUsage_CollectsNonAsciiCodepointsOnly()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter)
                .Replace("<p>Hello, world.</p>", "<p>Café naïve Café</p>", StringComparison.Ordinal));
        });

        // Act
        var rows = BookReportEngine.GetCharacterUsage(book);

        // Assert
        rows.Should().NotContain(r => r.CodePoint <= 0x7F);
        CharacterUsageRow eAcute = rows.Single(r => r.CodePoint == 0x00E9); // é
        eAcute.Count.Should().Be(2); // "Café" x2
        eAcute.FoundIn.Should().ContainSingle(p => p.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
    }

    [Fact]
    public void GetWordCharacterCounts_SumsPerFileAndTotal()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // Act
        WordCharacterCountsReport report = BookReportEngine.GetWordCharacterCounts(book);

        // Assert
        report.Files.Should().HaveCount(2); // chapter1.xhtml + nav.xhtml (GetHtmlResources returns both)
        report.TotalWords.Should().Be(report.Files.Sum(f => f.Words));
        report.TotalCharacters.Should().Be(report.Files.Sum(f => f.Characters));
        report.TotalWords.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetImageFiles_ReportsDimensionsFormatAndUsage()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMedia(temp);

        // Act
        var rows = BookReportEngine.GetImageFiles(book);

        // Assert
        ImageFilesRow figure = rows.Single(r => r.Name == "figure.png");
        figure.Format.Should().Be("PNG");
        figure.Width.Should().BeGreaterThan(0);
        figure.Height.Should().BeGreaterThan(0);
        figure.UsedIn.Should().ContainSingle(p => p.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

        ImageFilesRow cover = rows.Single(r => r.Name == "cover.png");
        cover.UsedIn.Should().BeEmpty();
    }

    [Fact]
    public void GetAllFiles_MediaCorpus_MatchesManualResourceCount()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMedia(temp);

        // Matches a manual count of corpus/epub3/media.
        // Manifest: nav.xhtml, package.opf, style.css, chapter1.xhtml, cover.png, figure.png, font.ttf, clip.mp3.
        // Act
        var rows = BookReportEngine.GetAllFiles(book);

        // Assert
        rows.Should().HaveCount(8);
        rows.Should().ContainSingle(r => r.Name == "clip.mp3" && r.TypeName == "Audio");
        rows.Should().ContainSingle(r => r.Name == "font.ttf" && r.TypeName == "Font");
    }
}
