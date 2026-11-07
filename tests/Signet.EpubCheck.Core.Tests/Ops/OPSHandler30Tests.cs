using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ops;

/// <summary>What the handler of the content documents of EPUB 3 reports: properties, semantics, media, viewport, accessibility.</summary>
public sealed class OPSHandler30Tests
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string MathNamespace = "http://www.w3.org/1998/Math/MathML";
    private const string FixedLayoutHead = "<meta name=\"viewport\" content=\"width=100, height=200\"/>";

    [Fact]
    public void Check_ScriptWithoutTheScriptedProperty_ReportsOpf014()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", head: "<script>var a;</script>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Should().ContainSingle().Which.Arguments.Should().Equal("scripted");
    }

    [Fact]
    public void Check_ScriptWithTheScriptedProperty_ReportsNothingAboutProperties()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", head: "<script>var a;</script>").WithItemProperty(PackageVocabs.ItemProperty.Scripted);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain([MessageId.OPF_014, MessageId.OPF_015]);
    }

    [Fact]
    public void Check_ScriptedPropertyWithoutScript_ReportsOpf015()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>").WithItemProperty(PackageVocabs.ItemProperty.Scripted);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_015).Should().ContainSingle().Which.Arguments.Should().Equal("scripted");
    }

    [Fact]
    public void Check_EventAttributeOrForm_NeedsTheScriptedProperty()
    {
        // Arrange
        ContentDocumentHarness handler = Document("<p onclick=\"go()\">x</p>");
        ContentDocumentHarness form = Document("<form action=\"#\"><p>x</p></form>");

        // Act
        Check(handler);
        Check(form);

        // Assert
        handler.Messages(MessageId.OPF_014).Should().ContainSingle();
        form.Messages(MessageId.OPF_014).Should().ContainSingle();
    }

    [Fact]
    public void Check_RemoteImage_NeedsTheRemoteResourcesProperty()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p><img src=\"https://example.org/a.png\" alt=\"\"/></p>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Should().ContainSingle().Which.Arguments.Should().Equal("remote-resources");
        harness.References.Should().ContainSingle().Which.Type.Should().Be(ReferenceType.Image);
    }

    [Theory]
    [InlineData(false, MessageId.OPF_018)]
    [InlineData(true, MessageId.OPF_018b)]
    public void Check_RemoteResourcesPropertyNotNeeded_ReportsOpf018(bool scripted, MessageId expected)
    {
        // Arrange
        string head = scripted ? "<script>var a;</script>" : string.Empty;
        ContentDocumentHarness harness = Document("<p>x</p>", head).WithItemProperty(PackageVocabs.ItemProperty.RemoteResources).WithItemProperty(PackageVocabs.ItemProperty.Scripted);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(expected);
        harness.Ids.Should().NotContain(scripted ? MessageId.OPF_018 : MessageId.OPF_018b);
    }

    [Fact]
    public void Check_SvgAndMathml_NeedTheirProperties()
    {
        // Arrange
        ContentDocumentHarness harness = Document(
            $"<svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"1\" height=\"1\"/><math xmlns=\"{MathNamespace}\" alttext=\"x\"><mi>x</mi></math>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Select(message => message.Arguments[0]).Should().BeEquivalentTo(new object[] { "svg", "mathml" });
    }

    [Fact]
    public void Check_MathWithoutAlternativeText_ReportsAcc009()
    {
        // Arrange
        ContentDocumentHarness harness = Document($"<math xmlns=\"{MathNamespace}\"><mi>x</mi></math>").WithItemProperty(PackageVocabs.ItemProperty.Mathml);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.ACC_009).Should().ContainSingle();
    }

    [Fact]
    public void Check_EpubSwitch_NeedsTheSwitchProperty()
    {
        // Arrange
        ContentDocumentHarness harness = Document(
            "<epub:switch><epub:case required-namespace=\"http://www.w3.org/1998/Math/MathML\"><p>a</p></epub:case><epub:default><p>b</p></epub:default></epub:switch>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Select(message => message.Arguments[0]).Should().Contain("switch");
    }

    [Fact]
    public void Check_UnknownStructuralSemantic_ReportsOpf088()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p epub:type=\"zzz\">x</p>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_088).Should().ContainSingle().Which.Arguments.Should().Equal("zzz");
    }

    [Fact]
    public void Check_UnknownPrefix_ReportsOpf028()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p epub:type=\"zzz:abc\">x</p>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_028).Should().ContainSingle();
    }

    [Fact]
    public void Check_PrefixDeclaredOnTheRoot_ChangesNothingForUnknownTypes()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p epub:type=\"zzz:abc\">x</p>", rootAttributes: " epub:prefix=\"zzz: http://example.org/vocab#\"");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain([MessageId.OPF_028, MessageId.OPF_088]);
    }

    [Fact]
    public void Check_PageBreakAndIndexAndGlossaryAndDictionary_AreReportedAsFeatures()
    {
        // Arrange
        ContentDocumentHarness harness = Document(
            "<span epub:type=\"pagebreak\" role=\"doc-pagebreak\" aria-label=\"1\"/><section epub:type=\"index\"><p>x</p></section><section epub:type=\"glossary\"><p>y</p></section><section epub:type=\"dictionary\"><p>z</p></section>");

        // Act
        Check(harness);

        // Assert
        FeatureReport features = harness.Context.FeatureReport;
        features.HasFeature(FeatureKind.PageBreak).Should().BeTrue();
        features.HasFeature(FeatureKind.Index).Should().BeTrue();
        features.HasFeature(FeatureKind.Dictionary).Should().BeTrue();
    }

    [Theory]
    [InlineData("index", PackageVocabs.ItemProperty.Index)]
    [InlineData("glossary", PackageVocabs.ItemProperty.Glossary)]
    public void Check_IndexOrGlossarySemantics_AllowTheMatchingProperty(string type, PackageVocabs.ItemProperty property)
    {
        // Arrange
        ContentDocumentHarness harness = Document($"<section epub:type=\"{type}\"><p>x</p></section>").WithItemProperty(property);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain(MessageId.OPF_015);
    }

    [Fact]
    public void Check_RegionBasedOutsideADataNavigationDocument_ReportsHtm052()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<nav epub:type=\"region-based\"><ol><li><a href=\"#a\">a</a></li></ol></nav>");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.HTM_052);
    }

    [Fact]
    public void Check_DocumentWithoutEpubType_ReportsAcc007()
    {
        // Arrange
        ContentDocumentHarness plain = Document("<p>x</p>");
        ContentDocumentHarness typed = Document("<section epub:type=\"chapter\"><p>x</p></section>");

        // Act
        Check(plain);
        Check(typed);

        // Assert
        plain.Ids.Should().Contain(MessageId.ACC_007);
        typed.Ids.Should().NotContain(MessageId.ACC_007);
    }

    [Fact]
    public void Check_AnchorWithoutText_ReportsAcc004ButNotWithText()
    {
        // Arrange
        ContentDocumentHarness empty = Document("<p><a href=\"#a\"></a></p>");
        ContentDocumentHarness withText = Document("<p><a href=\"#a\">a</a></p>");

        // Act
        Check(empty);
        Check(withText);

        // Assert
        empty.Ids.Should().Contain(MessageId.ACC_004);
        withText.Ids.Should().NotContain(MessageId.ACC_004);
    }

    [Theory]
    [InlineData("<base href=\"http://example.org/\"/>", "base")]
    [InlineData("<ruby>a<rp>(</rp><rt>b</rt><rp>)</rp></ruby>", "rp")]
    public void Check_DiscouragedElement_ReportsHtm055(string markup, string element)
    {
        // Arrange
        ContentDocumentHarness harness = Document(markup.StartsWith("<base", System.StringComparison.Ordinal) ? "<p>x</p>" : markup, head: markup.StartsWith("<base", System.StringComparison.Ordinal) ? markup : string.Empty);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_055).Select(message => message.Arguments[0]).Should().Contain(element);
    }

    [Fact]
    public void Check_EpubPrefixBoundToAnotherNamespace_ReportsHtm010()
    {
        // Arrange
        ContentDocumentHarness harness = new("chapter.xhtml", $"<html xmlns=\"{ContentDocumentHarness.XhtmlNamespace}\" xmlns:epub=\"http://example.org/ops\"><head><title>T</title></head><body><p>x</p></body></html>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_010).Should().NotBeEmpty();
    }

    [Fact]
    public void Check_SourceSetImages_AreAllRegistered()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p><img src=\"a.png\" srcset=\"b.png 1x, c.png 2x\" alt=\"\"/></p>")
            .With("a.png", "image/png").With("b.png", "image/png").With("c.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => System.IO.Path.GetFileName(reference.Url.Path)).Should().Equal("a.png", "b.png", "c.png");
        harness.References.Should().OnlyContain(reference => reference.Type == ReferenceType.Image && !reference.HasIntrinsicFallback);
    }

    [Fact]
    public void Check_PictureWithNonCoreImage_ReportsMed003AndMed007()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<picture><source srcset=\"a.bmp\"/><img src=\"b.bmp\" alt=\"\"/></picture>")
            .With("a.bmp", "image/bmp").With("b.bmp", "image/bmp");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.MED_003).Should().ContainSingle();
        harness.Messages(MessageId.MED_007).Should().ContainSingle();
        harness.References.Should().OnlyContain(reference => reference.HasIntrinsicFallback);
    }

    [Fact]
    public void Check_PictureSourceWithTypeThatDoesNotMatch_ReportsOpf013()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<picture><source srcset=\"a.png\" type=\"image/jpeg\"/><img src=\"a.png\" alt=\"\"/></picture>")
            .With("a.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_013).Should().ContainSingle();
    }

    [Fact]
    public void Check_AudioWithCoreSource_RegistersTheSourcesWithAFallback()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<audio controls=\"controls\"><source src=\"a.mp3\" type=\"audio/mpeg\"/><source src=\"a.wav\" type=\"audio/wav\"/></audio>")
            .With("a.mp3", "audio/mpeg").With("a.wav", "audio/wav");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().HaveCount(2).And.OnlyContain(reference => reference.Type == ReferenceType.Audio && reference.HasIntrinsicFallback);
        harness.Context.FeatureReport.HasFeature(FeatureKind.Audio).Should().BeTrue();
    }

    [Fact]
    public void Check_AudioWithoutCoreSource_RegistersTheSourcesWithoutAFallback()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<audio controls=\"controls\"><source src=\"a.wav\" type=\"audio/wav\"/></audio>")
            .With("a.wav", "audio/wav");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().ContainSingle().Which.HasIntrinsicFallback.Should().BeFalse();
    }

    [Fact]
    public void Check_AudioSourceWhoseTypeDiffersFromTheManifest_ReportsOpf013()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<audio controls=\"controls\"><source src=\"a.mp3\" type=\"audio/ogg\"/></audio>")
            .With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_013).Should().ContainSingle();
    }

    [Fact]
    public void Check_AudioSourceWithATypeThatDoesNotParse_ReportsOpf013InsteadOfFailing()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<audio controls=\"controls\"><source src=\"a.mp3\" type=\"not a type\"/></audio>")
            .With("a.mp3", "audio/mpeg");

        // Act
        System.Action act = () => Check(harness);

        // Assert
        act.Should().NotThrow();
        harness.Messages(MessageId.OPF_013).Should().ContainSingle();
    }

    [Fact]
    public void Check_VideoAndPoster_AreRegistered()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<video src=\"v.mp4\" poster=\"p.png\"></video>")
            .With("v.mp4", "video/mp4").With("p.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.Image, ReferenceType.Video);
        harness.Context.FeatureReport.HasFeature(FeatureKind.Video).Should().BeTrue();
    }

    [Theory]
    [InlineData("<object data=\"a.swf\" type=\"application/x-shockwave-flash\"><p>fallback text</p></object>", true)]
    [InlineData("<object data=\"a.swf\" type=\"application/x-shockwave-flash\"></object>", false)]
    [InlineData("<object data=\"a.swf\" type=\"application/x-shockwave-flash\"><img src=\"a.png\" alt=\"\"/></object>", true)]
    public void Check_Object_HasAnIntrinsicFallbackWhenItHasPalpableContent(string markup, bool expected)
    {
        // Arrange
        ContentDocumentHarness harness = Document("<div>" + markup + "</div>").With("a.swf", "application/x-shockwave-flash").With("a.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Single(reference => reference.Url.Path!.EndsWith("a.swf", System.StringComparison.Ordinal)).HasIntrinsicFallback.Should().Be(expected);
    }

    [Fact]
    public void Check_ObjectWithATypeThatDiffersFromTheManifest_ReportsOpf013()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<div><object data=\"a.png\" type=\"image/jpeg\"><p>x</p></object></div>").With("a.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_013).Should().ContainSingle();
    }

    [Fact]
    public void Check_SvgResourceInHtml_AllowsTheSvgProperty()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p><img src=\"a.svg\" alt=\"\"/></p>").WithItemProperty(PackageVocabs.ItemProperty.Svg).With("a.svg", ContentDocumentHarness.Svg);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain([MessageId.OPF_014, MessageId.OPF_015]);
    }

    [Fact]
    public void Check_FixedLayoutWithoutViewport_ReportsHtm046()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", fixedLayout: true);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.HTM_046);
    }

    [Fact]
    public void Check_FixedLayoutWithAValidViewport_ReportsNoViewportProblem()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", FixedLayoutHead, fixedLayout: true);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain([MessageId.HTM_046, MessageId.HTM_047, MessageId.HTM_056, MessageId.HTM_057, MessageId.HTM_059, MessageId.HTM_060a, MessageId.HTM_060b]);
    }

    [Theory]
    [InlineData("width=100", MessageId.HTM_056)]
    [InlineData("width=abc, height=200", MessageId.HTM_057)]
    [InlineData("", MessageId.HTM_047)]
    [InlineData("width=100, width=200, height=200", MessageId.HTM_059)]
    public void Check_FixedLayoutWithAWrongViewport_ReportsIt(string content, MessageId expected)
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", $"<meta name=\"viewport\" content=\"{content}\"/>", fixedLayout: true);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(expected);
    }

    [Fact]
    public void Check_SecondViewportInFixedLayout_ReportsHtm060a()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", FixedLayoutHead + FixedLayoutHead, fixedLayout: true);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_060a).Should().ContainSingle();
    }

    [Fact]
    public void Check_ViewportInReflowableDocument_ReportsHtm060b()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", FixedLayoutHead);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_060b).Should().ContainSingle();
    }

    [Fact]
    public void Check_FixedLayoutSvgWithoutViewBox_ReportsHtm048()
    {
        // Arrange
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", $"<svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"10\" height=\"10\"/>", ContentDocumentHarness.Svg) { FixedLayout = true };

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_048).Should().ContainSingle();
    }

    [Fact]
    public void Check_AlternateStyleSheetLinkWithoutTitle_ReportsCss015()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", "<link rel=\"alternate stylesheet\" href=\"s.css\"/>").With("s.css", "text/css");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.CSS_015).Should().ContainSingle();
    }

    [Theory]
    [InlineData("vertical horizontal", true)]
    [InlineData("day night", true)]
    [InlineData("vertical day", false)]
    public void Check_ContradictoryClassesOfAStyleSheetLink_ReportCss005(string classes, bool reported)
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>", $"<link rel=\"alternate stylesheet\" class=\"{classes}\" title=\"t\" href=\"s.css\"/>").With("s.css", "text/css");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Contains(MessageId.CSS_005).Should().Be(reported);
    }

    [Fact]
    public void Check_MicrodataAndRdfa_AreReportedAsFeatures()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p itemscope=\"itemscope\" itemtype=\"http://schema.org/Thing\">x</p><p property=\"dc:title\">y</p>");

        // Act
        Check(harness);

        // Assert
        harness.Context.FeatureReport.HasFeature(FeatureKind.HasMicrodata).Should().BeTrue();
        harness.Context.FeatureReport.HasFeature(FeatureKind.HasRdfa).Should().BeTrue();
    }

    [Fact]
    public void Check_TableAndFigure_AreReportedAsFeatures()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<table><tr><td>x</td></tr></table><figure><p>y</p></figure>");

        // Act
        Check(harness);

        // Assert
        harness.Context.FeatureReport.HasFeature(FeatureKind.Table).Should().BeTrue();
        harness.Context.FeatureReport.HasFeature(FeatureKind.Figure).Should().BeTrue();
    }

    [Fact]
    public void Check_SectionsOfAnEdupubPublication_AreReportedAsFeatures()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<section><h1>a</h1></section><section><h1>b</h1></section>");
        harness.Profile = EpubProfile.Edupub;

        // Act
        Check(harness);

        // Assert
        harness.Context.FeatureReport.GetFeature(FeatureKind.Sections).Should().HaveCount(2);
    }

    [Fact]
    public void Check_MediaOverlayActiveClassWithoutCss_ReportsCss030()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p>x</p>");
        harness.MediaOverlay = "overlay";
        harness.Build();
        harness.Context.FeatureReport.Report(FeatureKind.MediaOverlaysActiveClass, new EpubLocation("package.opf"));

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Messages(MessageId.CSS_030).Should().ContainSingle();
    }

    [Fact]
    public void Check_IframeAndEmbedAndTrackAndInput_RegisterTheirSources()
    {
        // Arrange
        ContentDocumentHarness harness = Document(
            "<iframe src=\"f.xhtml\"></iframe><embed src=\"e.swf\" type=\"application/x-shockwave-flash\"/><video><track src=\"t.vtt\" kind=\"captions\"/></video><input type=\"image\" src=\"i.png\" alt=\"\"/>")
            .With("f.xhtml", ContentDocumentHarness.Xhtml).With("e.swf", "application/x-shockwave-flash").With("t.vtt", "text/vtt").With("i.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => (System.IO.Path.GetFileName(reference.Url.Path), reference.Type)).Should().BeEquivalentTo(
        [
            ("f.xhtml", ReferenceType.Generic),
            ("e.swf", ReferenceType.Generic),
            ("t.vtt", ReferenceType.Track),
            ("i.png", ReferenceType.Generic),
        ]);
    }

    [Fact]
    public void Check_CiteAttributes_AreRegisteredAsCites()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<blockquote cite=\"q.xhtml\"><p>x</p></blockquote>").With("q.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Should().ContainSingle().Which.Type.Should().Be(ReferenceType.Cite);
    }

    [Fact]
    public void Check_DataUrlHyperlink_ReportsRsc029()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p><a href=\"data:text/plain,x\">x</a></p>");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_029);
    }

    [Fact]
    public void Check_SvgHrefAndXlinkHref_AreReportedWhenTheyDiffer()
    {
        // Arrange
        string svg = $"<svg xmlns=\"{SvgNamespace}\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" version=\"1.1\" width=\"10\" height=\"10\"><image xlink:href=\"a.png\" width=\"1\" height=\"1\"/><image href=\"a.png\" xlink:href=\"b.png\" width=\"1\" height=\"1\"/></svg>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg).With("a.png", "image/png").With("b.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.HTM_062).Should().ContainSingle();
        harness.Messages(MessageId.HTM_063).Should().ContainSingle();
    }

    [Fact]
    public void Check_ImageSourceThatDoesNotParse_DoesNotFail()
    {
        // Arrange
        ContentDocumentHarness harness = Document("<p><img src=\"http://[bad\" alt=\"\"/><audio src=\"http://[bad\"></audio></p>");

        // Act
        System.Action act = () => Check(harness);

        // Assert
        act.Should().NotThrow();
        harness.Ids.Should().Contain(MessageId.RSC_020);
    }

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new OPSChecker(harness.Context).Check();
    }

    private static ContentDocumentHarness Document(string body, string head = "", string rootAttributes = "", bool fixedLayout = false) =>
        new("chapter.xhtml", ContentDocumentHarness.Html(body, head, rootAttributes)) { FixedLayout = fixedLayout };
}
