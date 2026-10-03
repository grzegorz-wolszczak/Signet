using System;
using System.IO;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <c>CleanSource.PrettyPrint</c> / <see cref="CleanSource.PrettyPrintXml"/> — XHTML/XML formatting.</summary>
public sealed class PrettyPrintTests
{
    private static readonly string FixtureDir = Path.Combine(CorpusPaths.Root, "prettyprint");

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(FixtureDir, name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    public static TheoryData<string> AllCorpusXhtml()
    {
        string malformedDir = $"{Path.DirectorySeparatorChar}malformed{Path.DirectorySeparatorChar}";
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.xhtml", SearchOption.AllDirectories))
        {
            if (!path.Contains(malformedDir, StringComparison.Ordinal))
            {
                data.Add(path);
            }
        }

        return data;
    }

    // =====================================================================
    //  Snapshoty vs golden (tests/corpus/prettyprint)
    // =====================================================================

    [Fact]
    public void PrettyPrint_MessyXhtml_MatchesGolden()
    {
        string result = CleanSource.PrettyPrint(ReadFixture("messy-in.xhtml"), keepWhitespace: false, "3.0");

        result.Should().Be(ReadFixture("messy-out.xhtml"));
    }

    [Fact]
    public void PrettyPrint_Epub2WithEpubType_MatchesGolden_AndKeepsNamespacedAttributes()
    {
        string result = CleanSource.PrettyPrint(ReadFixture("epubtype-in.xhtml"), keepWhitespace: false, "2.0");

        result.Should().Be(ReadFixture("epubtype-out.xhtml"));
        result.Should().Contain("epub:type=\"chapter\"").And.Contain("role=\"doc-chapter\"");
        result.Should().Contain("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"");
    }

    [Fact]
    public void PrettyPrintXml_Opf_MatchesGolden()
    {
        string result = CleanSource.PrettyPrintXml(ReadFixture("opf-in.xml"), "application/oebps-package+xml");

        result.Should().Be(ReadFixture("opf-out.xml"));
    }

    [Fact]
    public void PrettyPrintXml_Ncx_MatchesGolden()
    {
        string result = CleanSource.PrettyPrintXml(ReadFixture("ncx-in.xml"), "application/x-dtbncx+xml");

        result.Should().Be(ReadFixture("ncx-out.xml"));
    }

    // =====================================================================
    //  Idempotencja
    // =====================================================================

    [Theory]
    [InlineData("messy-out.xhtml", "3.0")]
    [InlineData("epubtype-out.xhtml", "2.0")]
    public void PrettyPrint_IsIdempotent_OnGolden(string goldenName, string version)
    {
        string once = ReadFixture(goldenName);

        CleanSource.PrettyPrint(once, keepWhitespace: false, version).Should().Be(once);
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void PrettyPrint_EveryCorpusXhtml_IsWellFormedAndIdempotent(string xhtmlPath)
    {
        string source = File.ReadAllText(xhtmlPath);

        string once = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        WellFormedChecker.Check(once).IsWellFormed.Should().BeTrue();
        CleanSource.PrettyPrint(once, keepWhitespace: false, "3.0").Should().Be(once);
    }

    [Fact]
    public void PrettyPrintXml_EveryCorpusPackageAndNcx_IsWellFormedAndIdempotent()
    {
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.*", SearchOption.AllDirectories))
        {
            string mediaType = path.EndsWith(".opf", StringComparison.Ordinal)
                ? "application/oebps-package+xml"
                : path.EndsWith(".ncx", StringComparison.Ordinal)
                    ? "application/x-dtbncx+xml"
                    : null!;

            if (mediaType is null || path.Contains($"{Path.DirectorySeparatorChar}malformed{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string once = CleanSource.PrettyPrintXml(File.ReadAllText(path), mediaType);

            WellFormedChecker.Check(once).IsWellFormed.Should().BeTrue(path);
            CleanSource.PrettyPrintXml(once, mediaType).Should().Be(once, path);
        }
    }

    // =====================================================================
    //  Formatting rules
    // =====================================================================

    [Fact]
    public void PrettyPrint_PreservesPreContentCharForChar()
    {
        const string source =
            "<html><head><title>t</title></head><body><pre>line1\n  line2\n\tline3\n</pre></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        result.Should().Contain("<pre>line1\n  line2\n\tline3\n</pre>");
    }

    [Fact]
    public void PrettyPrint_DoesNotBreakInlineElements()
    {
        const string source =
            "<html><head><title>t</title></head><body><p>This is <b>bold</b> and <a href=\"x\">a link</a> here.</p></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        result.Should().Contain("<p>This is <b>bold</b> and <a href=\"x\">a link</a> here.</p>");
    }

    [Fact]
    public void PrettyPrint_KeepsAttributesOnOneLine()
    {
        const string source =
            "<html><head><title>t</title></head><body><p id=\"a\" class=\"b c\" data-x=\"y\">t</p></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        result.Should().Contain("<p id=\"a\" class=\"b c\" data-x=\"y\">t</p>");
    }

    [Fact]
    public void PrettyPrint_InjectsEmptyTitleIntoTitlelessHead()
    {
        string result = CleanSource.PrettyPrint("<html><head></head><body><p>x</p></body></html>", keepWhitespace: false, "3.0");

        result.Should().Contain("<title></title>");
    }

    [Fact]
    public void PrettyPrint_KeepWhitespace_DoesNotCondenseRuns()
    {
        const string source = "<html><head><title>t</title></head><body><p>keep    these     spaces</p></body></html>";

        string kept = CleanSource.PrettyPrint(source, keepWhitespace: true, "3.0");
        string condensed = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        kept.Should().Contain("keep    these     spaces");
        condensed.Should().Contain("keep these spaces");
    }

    [Fact]
    public void PrettyPrint_VoidElements_SelfCloseWithoutSpace()
    {
        const string source =
            "<html><head><title>t</title><link rel=\"stylesheet\" href=\"s.css\"/></head><body><p>a<br/>b<img src=\"i.png\" alt=\"\"/></p></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0");

        result.Should().Contain("<br/>");
        result.Should().Contain("<img src=\"i.png\" alt=\"\"/>");
        result.Should().NotContain("<br />");
    }

    [Fact]
    public void PrettyPrint_EmptySource_IsReturnedUnchanged()
    {
        CleanSource.PrettyPrint(string.Empty, keepWhitespace: false, "3.0").Should().BeEmpty();
    }

    [Fact]
    public void PrettyPrint_CustomIndentAndSingleSpace_AreHonoured()
    {
        const string source = "<html><head><title>t</title></head><body><div><p>x</p></div></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0",
            new PrettyPrintOptions(IndentString: "    ", SingleSpace: true));

        // div (lvl 2) → indent 4, p (lvl 3) → indent 8; singlespace: no empty line between blocks.
        result.Should().Contain("    <div>\n        <p>x</p>\n    </div>\n</body>");
    }

    // =====================================================================
    //  PrettyPrintXml — przypadki brzegowe
    // =====================================================================

    [Fact]
    public void PrettyPrintXml_NcxWithoutNcxTag_IsReturnedUnchanged()
    {
        const string source = "<?xml version=\"1.0\"?>\n<notncx><x/></notncx>";

        CleanSource.PrettyPrintXml(source, "application/x-dtbncx+xml").Should().Be(source);
    }

    [Fact]
    public void PrettyPrintXml_NotWellFormed_IsReturnedUnchanged()
    {
        const string source = "<?xml version=\"1.0\"?>\n<package><metadata><spine></package>";

        CleanSource.PrettyPrintXml(source, "application/oebps-package+xml").Should().Be(source);
    }

    [Fact]
    public void PrettyPrintXml_EmptyNonVoidElement_IsWrittenAsOpenClosePair()
    {
        const string source =
            "<?xml version=\"1.0\"?>\n" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            "<metadata><dc:title></dc:title></metadata><manifest><item id=\"a\" href=\"a.xhtml\"/></manifest></package>";

        string result = CleanSource.PrettyPrintXml(source, "application/oebps-package+xml");

        result.Should().Contain("<dc:title></dc:title>");   // dc:title is not "void" for OPF
        result.Should().Contain("<item id=\"a\" href=\"a.xhtml\" />");   // item is "void"
    }

    // =====================================================================
    //  PrettyPrintProps
    // =====================================================================

    [Fact]
    public void PrettyPrintProps_Default_ClassifiesTagsPerDefaults()
    {
        PrettyPrintProps props = PrettyPrintProps.Default;

        props.IsStructural("div").Should().BeTrue();
        props.IsInline("span").Should().BeTrue();
        props.IsVoid("br").Should().BeTrue();
        props.IsPreserveSpace("pre").Should().BeTrue();
        props.IsNoEntitySub("script").Should().BeTrue();
        props.IsTextHolder("p").Should().BeTrue();
        props.IsInline("div").Should().BeFalse();
    }

    [Fact]
    public void PrettyPrintProps_LoadFromFile_MissingFile_WritesDefaultAndReturnsDefault()
    {
        string dir = Path.Combine(Path.GetTempPath(), "signet-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "prettyprint.xml");
        try
        {
            PrettyPrintProps props = PrettyPrintProps.LoadFromFile(path);

            File.Exists(path).Should().BeTrue();
            props.Should().BeSameAs(PrettyPrintProps.Default);
            File.ReadAllText(path).Should().Contain("<structural_tags>");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrettyPrintProps_LoadFromFile_CustomIndentAndTagSets_AreApplied()
    {
        string dir = Path.Combine(Path.GetTempPath(), "signet-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "prettyprint.xml");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<prettyprint>\n"
                + "  <indent_string>\"    \"</indent_string>\n"
                + "  <singlespace>true</singlespace>\n"
                + "  <structural_tags>div, custom-block</structural_tags>\n"
                + "  <noentitysub_tags>custom-noentity</noentitysub_tags>\n"
                + "</prettyprint>\n");

            PrettyPrintProps props = PrettyPrintProps.LoadFromFile(path);

            props.IndentString.Should().Be("    ");
            props.SingleSpace.Should().BeTrue();
            props.IsStructural("custom-block").Should().BeTrue();
            props.IsStructural("body").Should().BeFalse("the file overrode the whole structural_tags group, it did not append to it");
            props.IsNoEntitySub("custom-noentity").Should().BeTrue(
                "the override of noentitysub_tags is applied correctly");
            props.IsInline("span").Should().BeTrue("groups absent from the file stay the built-in default set");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrettyPrintProps_LoadFromFile_MalformedXml_FallsBackToDefault()
    {
        string dir = Path.Combine(Path.GetTempPath(), "signet-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "prettyprint.xml");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(path, "<prettyprint><unclosed>");

            PrettyPrintProps props = PrettyPrintProps.LoadFromFile(path);

            props.Should().BeSameAs(PrettyPrintProps.Default);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
