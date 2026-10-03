using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Tests of <see cref="NcxDocument"/> — a lenient NCX parser into an in-memory model and the serialization
/// back.
/// </summary>
public sealed class NcxDocumentTests
{
    private const string NestedNcx =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">\n" +
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n" +
        "  <head>\n" +
        "    <meta name=\"dtb:uid\" content=\"urn:uuid:abc\"/>\n" +
        "    <meta name=\"dtb:depth\" content=\"1\"/>\n" +
        "    <meta name=\"dtb:totalPageCount\" content=\"0\"/>\n" +
        "    <meta name=\"dtb:maxPageNumber\" content=\"0\"/>\n" +
        "  </head>\n" +
        "  <docTitle><text>Nested Book</text></docTitle>\n" +
        "  <navMap>\n" +
        "    <navPoint id=\"np1\" playOrder=\"7\">\n" +
        "      <navLabel><text>Part I</text></navLabel>\n" +
        "      <content src=\"Text/part1.xhtml\"/>\n" +
        "      <navPoint id=\"np2\" playOrder=\"9\">\n" +
        "        <navLabel><text>Chapter 1</text></navLabel>\n" +
        "        <content src=\"Text/ch1.xhtml\"/>\n" +
        "        <navPoint id=\"np3\" playOrder=\"3\">\n" +
        "          <navLabel><text>Section 1.1</text></navLabel>\n" +
        "          <content src=\"Text/ch1.xhtml#s11\"/>\n" +
        "        </navPoint>\n" +
        "      </navPoint>\n" +
        "    </navPoint>\n" +
        "    <navPoint id=\"np4\" playOrder=\"1\">\n" +
        "      <navLabel><text>Part II</text></navLabel>\n" +
        "      <content src=\"Text/part2.xhtml\"/>\n" +
        "    </navPoint>\n" +
        "  </navMap>\n" +
        "</ncx>\n";

    private const string PageAndNavListNcx =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n" +
        "  <head><meta name=\"dtb:uid\" content=\"x\"/></head>\n" +
        "  <docTitle><text>T</text></docTitle>\n" +
        "  <navMap>\n" +
        "    <navPoint id=\"n1\" playOrder=\"1\"><navLabel><text>One</text></navLabel><content src=\"a.xhtml\"/></navPoint>\n" +
        "  </navMap>\n" +
        "  <pageList>\n" +
        "    <pageTarget id=\"p1\" type=\"normal\" value=\"1\" playOrder=\"2\">\n" +
        "      <navLabel><text>1</text></navLabel>\n" +
        "      <content src=\"a.xhtml#p1\"/>\n" +
        "    </pageTarget>\n" +
        "    <pageTarget id=\"p2\" type=\"normal\" value=\"2\" playOrder=\"3\">\n" +
        "      <navLabel><text>2</text></navLabel>\n" +
        "      <content src=\"a.xhtml#p2\"/>\n" +
        "    </pageTarget>\n" +
        "  </pageList>\n" +
        "  <navList id=\"loi\" class=\"list-of-illustrations\">\n" +
        "    <navLabel><text>List of Illustrations</text></navLabel>\n" +
        "    <navTarget id=\"ill1\" playOrder=\"4\">\n" +
        "      <navLabel><text>Figure 1</text></navLabel>\n" +
        "      <content src=\"a.xhtml#fig1\"/>\n" +
        "    </navTarget>\n" +
        "  </navList>\n" +
        "</ncx>\n";

    private static string ReadCorpusNcx(string corpusDir) =>
        File.ReadAllText(Directory.EnumerateFiles(corpusDir, "*.ncx", SearchOption.AllDirectories).Single());

    // --- korpus ---

    [Fact]
    public void Parses_epub2_minimal_corpus_ncx()
    {
        NcxDocument ncx = NcxDocument.Parse(ReadCorpusNcx(CorpusPaths.Epub2Minimal));

        ncx.DocTitle.Should().Be("Minimal EPUB 2");
        ncx.DtbUid.Should().Be("urn:uuid:22222222-2222-4222-8222-222222222222");
        ncx.IncludeDoctype.Should().BeTrue();
        ncx.Head.Should().HaveCount(4);
        ncx.NavMap.Should().ContainSingle();
        ncx.NavMap[0].Label.Should().Be("Chapter 1");
        ncx.NavMap[0].ContentSrc.Should().Be("Text/chapter1.xhtml");
        ncx.Depth.Should().Be(1);
    }

    [Fact]
    public void Parses_epub3_with_ncx_corpus_two_navpoints()
    {
        NcxDocument ncx = NcxDocument.Parse(ReadCorpusNcx(CorpusPaths.Epub3WithNcx));

        ncx.NavMap.Select(np => np.Label).Should().Equal("Chapter 1", "Chapter 2");
        ncx.NavMap.Select(np => np.ContentSrc).Should().Equal("text/chapter1.xhtml", "text/chapter2.xhtml");
    }

    [Theory]
    [InlineData("epub2/minimal")]
    [InlineData("epub3/with-ncx")]
    public void Round_trips_corpus_ncx(string which)
    {
        string dir = which == "epub2/minimal" ? CorpusPaths.Epub2Minimal : CorpusPaths.Epub3WithNcx;
        string source = ReadCorpusNcx(dir);

        NcxDocument first = NcxDocument.Parse(source);
        string xml = first.ToXml();
        NcxDocument second = NcxDocument.Parse(xml);

        second.ToXml().Should().Be(xml);
        Flatten(second).Should().Equal(Flatten(first));
        second.DocTitle.Should().Be(first.DocTitle);
        second.DtbUid.Should().Be(first.DtbUid);
    }

    // --- nested tree ---

    [Fact]
    public void Parses_nested_navpoint_tree()
    {
        NcxDocument ncx = NcxDocument.Parse(NestedNcx);

        ncx.NavMap.Should().HaveCount(2);
        ncx.NavMap[0].Label.Should().Be("Part I");
        ncx.NavMap[0].Children.Should().ContainSingle();
        ncx.NavMap[0].Children[0].Label.Should().Be("Chapter 1");
        ncx.NavMap[0].Children[0].Children[0].Label.Should().Be("Section 1.1");
        ncx.NavMap[0].Children[0].Children[0].ContentSrc.Should().Be("Text/ch1.xhtml#s11");
        ncx.Depth.Should().Be(3);
    }

    [Fact]
    public void ToXml_recomputes_playorder_in_preorder()
    {
        NcxDocument ncx = NcxDocument.Parse(NestedNcx);
        NcxDocument round = NcxDocument.Parse(ncx.ToXml());

        // preorder: Part I=1, Chapter 1=2, Section 1.1=3, Part II=4
        round.NavMap[0].PlayOrder.Should().Be(1);
        round.NavMap[0].Children[0].PlayOrder.Should().Be(2);
        round.NavMap[0].Children[0].Children[0].PlayOrder.Should().Be(3);
        round.NavMap[1].PlayOrder.Should().Be(4);
    }

    [Fact]
    public void ToXml_fills_missing_navpoint_id_from_playorder()
    {
        NcxDocument ncx = new();
        ncx.Head.Add(new NcxMeta("dtb:uid", "x"));
        ncx.NavMap.Add(new NcxNavPoint { Label = "Only", ContentSrc = "a.xhtml" });

        ncx.ToXml().Should().Contain("<navPoint id=\"navPoint-1\" playOrder=\"1\">");
    }

    // --- pageList / navList ---

    [Fact]
    public void Round_trips_pagelist_and_navlist()
    {
        NcxDocument first = NcxDocument.Parse(PageAndNavListNcx);

        first.PageList.Should().HaveCount(2);
        first.PageList[0].Type.Should().Be("normal");
        first.PageList[0].Value.Should().Be("1");
        first.PageList[0].ContentSrc.Should().Be("a.xhtml#p1");
        first.NavLists.Should().ContainSingle();
        first.NavLists[0].Id.Should().Be("loi");
        first.NavLists[0].Label.Should().Be("List of Illustrations");
        first.NavLists[0].Targets.Should().ContainSingle();
        first.NavLists[0].Targets[0].Label.Should().Be("Figure 1");

        string xml = first.ToXml();
        NcxDocument second = NcxDocument.Parse(xml);
        second.ToXml().Should().Be(xml);
        Flatten(second).Should().Equal(Flatten(first));
    }

    [Fact]
    public void Shared_playorder_counter_spans_navmap_pagelist_navlist()
    {
        NcxDocument ncx = NcxDocument.Parse(PageAndNavListNcx);
        ncx.RecomputePlayOrder();

        ncx.NavMap[0].PlayOrder.Should().Be(1);
        ncx.PageList[0].PlayOrder.Should().Be(2);
        ncx.PageList[1].PlayOrder.Should().Be(3);
        ncx.NavLists[0].Targets[0].PlayOrder.Should().Be(4);
    }

    // --- empty NCX / CreateEmpty ---

    [Fact]
    public void CreateEmpty_round_trips_with_empty_navmap()
    {
        NcxDocument created = NcxDocument.CreateEmpty("My Book", "urn:uuid:1");
        string xml = created.ToXml();
        NcxDocument parsed = NcxDocument.Parse(xml);

        parsed.DocTitle.Should().Be("My Book");
        parsed.DtbUid.Should().Be("urn:uuid:1");
        parsed.IncludeDoctype.Should().BeTrue();
        parsed.NavMap.Should().BeEmpty();
        parsed.Depth.Should().Be(0);
        parsed.ToXml().Should().Be(xml);
    }

    [Fact]
    public void CreateEmpty_epub3_omits_doctype()
    {
        NcxDocument created = NcxDocument.CreateEmpty("B", "u", epub2: false);

        created.ToXml().Should().NotContain("<!DOCTYPE");
    }

    [Fact]
    public void Parse_without_doctype_keeps_doctype_off_on_write()
    {
        string noDoctype = NestedNcx.Replace(
            "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">\n",
            string.Empty,
            System.StringComparison.Ordinal);

        NcxDocument ncx = NcxDocument.Parse(noDoctype);

        ncx.IncludeDoctype.Should().BeFalse();
        ncx.ToXml().Should().NotContain("<!DOCTYPE");
    }

    // --- kodowanie encji ---

    [Fact]
    public void Decodes_and_reencodes_xml_entities_in_labels()
    {
        string src = NestedNcx.Replace("Part I", "Tom &amp; Jerry &lt;b&gt;", System.StringComparison.Ordinal);

        NcxDocument ncx = NcxDocument.Parse(src);
        ncx.NavMap[0].Label.Should().Be("Tom & Jerry <b>");

        string xml = ncx.ToXml();
        xml.Should().Contain("<text>Tom &amp; Jerry &lt;b&gt;</text>");
        NcxDocument.Parse(xml).NavMap[0].Label.Should().Be("Tom & Jerry <b>");
    }

    [Fact]
    public void SyncHeadCounts_updates_depth_and_page_totals()
    {
        NcxDocument ncx = NcxDocument.Parse(PageAndNavListNcx);
        ncx.SyncHeadCounts();

        ncx.Head.Single(m => m.Name == "dtb:depth").Content.Should().Be("1");
        ncx.Head.Single(m => m.Name == "dtb:totalPageCount").Content.Should().Be("2");
        ncx.Head.Single(m => m.Name == "dtb:maxPageNumber").Content.Should().Be("2");
    }

    private static System.Collections.Generic.List<string> Flatten(NcxDocument doc)
    {
        System.Collections.Generic.List<string> lines = new();
        void Walk(NcxNavPoint np, int depth)
        {
            lines.Add($"NP{depth} {np.Label} -> {np.ContentSrc}");
            foreach (NcxNavPoint child in np.Children)
            {
                Walk(child, depth + 1);
            }
        }

        foreach (NcxNavPoint np in doc.NavMap)
        {
            Walk(np, 0);
        }

        foreach (NcxPageTarget pt in doc.PageList)
        {
            lines.Add($"PT {pt.Type}/{pt.Value} {pt.Label} -> {pt.ContentSrc}");
        }

        foreach (NcxNavList nl in doc.NavLists)
        {
            lines.Add($"NL {nl.Id} {nl.Label}");
            foreach (NcxNavTarget nt in nl.Targets)
            {
                lines.Add($"NT {nt.Label} -> {nt.ContentSrc}");
            }
        }

        return lines;
    }
}
