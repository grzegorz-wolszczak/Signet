using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Reporting;

public sealed class ReportWritersTests
{
    private const string Book = "book.epub";
    private const string JhoveNamespace = "http://schema.openpreservation.org/ois/xml/ns/jhove";
    private const string PremisNamespace = "http://www.loc.gov/premis/rdf/v1#";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    private static T Make<T>(T report)
        where T : ReportBase
    {
        report.SetLocale(s_english);
        report.Initialize();
        return report;
    }

    private static JsonNode Json(StringWriter text) => JsonNode.Parse(text.ToString())!;

    [Fact]
    public void JsonWriter_WritesTheLayoutOfJackson()
    {
        // Arrange
        StringWriter text = new();
        JacksonStyleJsonWriter sut = new(text);

        // Act
        sut.BeginObject();
        sut.Name("a");
        sut.StringArray(["x", "y"]);
        sut.Name("empty");
        sut.StringArray([]);
        sut.Name("none");
        sut.BeginObject();
        sut.EndObject();
        sut.Name("items");
        sut.BeginArray();
        sut.BeginObject();
        sut.Name("n");
        sut.Number(1);
        sut.Name("s");
        sut.String(null);
        sut.EndObject();
        sut.BeginObject();
        sut.Name("b");
        sut.Boolean(true);
        sut.EndObject();
        sut.EndArray();
        sut.EndObject();

        // Assert
        text.ToString().Should().Be(
            "{\n  \"a\" : [ \"x\", \"y\" ],\n  \"empty\" : [ ],\n  \"none\" : { },\n  \"items\" : [ {\n    \"n\" : 1,\n    \"s\" : null\n  }, {\n    \"b\" : true\n  } ]\n}");
    }

    [Fact]
    public void JsonWriter_EscapesWhatIsNotPlainAscii()
    {
        // Arrange
        StringWriter text = new();
        JacksonStyleJsonWriter sut = new(text);

        // Act
        sut.String("quote\" back\\ line\n tab\t \u00E9 \u0001");

        // Assert
        text.ToString().Should().Be("\"quote\\\" back\\\\ line\\n tab\\t \\u00E9 \\u0001\"");
    }

    [Fact]
    public void CheckMessage_GroupsByIdAndText_AndCountsTheLocationsBeyondTheLimit()
    {
        // Arrange
        StringWriter text = new();
        CheckingReport sut = Make(new CheckingReport(text, Book, closeWriter: false));
        sut.MaxOfEachMessage = 2;

        // Act
        for (int line = 1; line <= 5; line++)
        {
            sut.Message(MessageId.RSC_005, new EpubLocation("a.xhtml", line, 1), "same");
        }

        sut.Message(MessageId.RSC_005, new EpubLocation("b.xhtml"), "other");
        sut.Generate();

        // Assert
        JsonArray messages = Json(text)["messages"]!.AsArray();
        messages.Should().HaveCount(2);
        JsonNode same = messages.Single(m => m!["message"]!.GetValue<string>().Contains("same", StringComparison.Ordinal))!;
        same["locations"]!.AsArray().Should().HaveCount(2);
        same["additionalLocations"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void Json_StartsWithTheCustomMessageFile_AndCarriesTheCounts()
    {
        // Arrange
        StringWriter text = new();
        CheckingReport sut = Make(new CheckingReport(text, Book, closeWriter: false));
        sut.CustomMessageFile = "custom.txt";
        sut.Message(MessageId.RSC_005, new EpubLocation("a.xhtml", 1, 2), "boom");
        sut.Message(MessageId.PKG_010, new EpubLocation("b.xhtml"));

        // Act
        sut.Generate();

        // Assert
        JsonObject root = Json(text).AsObject();
        root.Select(p => p.Key).Should().Equal("customMessageFileName", "checker", "publication", "items", "messages");
        root["customMessageFileName"]!.GetValue<string>().Should().Be("custom.txt");
        root["checker"]!["nError"]!.GetValue<int>().Should().Be(1);
        root["checker"]!["nWarning"]!.GetValue<int>().Should().Be(1);
        root["messages"]!.AsArray().Select(m => m!["ID"]!.GetValue<string>()).Should().Equal("PKG-010", "RSC-005");
    }

    [Fact]
    public void Json_GivesTheItemsAnIdAndTheDefaultsOfThePublication()
    {
        // Arrange
        StringWriter text = new();
        CheckingReport sut = Make(new CheckingReport(text, Book, closeWriter: false));
        sut.Info(null, FeatureKind.RenditionLayout, "pre-paginated");
        sut.Info("page.xhtml", FeatureKind.IsSpineitem, "true");
        sut.Info("page.xhtml", FeatureKind.DeclaredMimetype, "application/xhtml+xml");
        sut.Info("page.xhtml", FeatureKind.Resource, "page.xhtml");
        sut.Info("page.xhtml", FeatureKind.Resource, "other.xhtml");
        sut.Info("other.xhtml", FeatureKind.UniqueIdent, "other");

        // Act
        sut.Generate();

        // Assert
        JsonArray items = Json(text)["items"]!.AsArray();
        items.Select(i => i!["id"]!.GetValue<string>()).Should().Equal("ePubCheck.NoManifestRef:page.xhtml", "other");
        JsonNode page = items[0]!;
        page["renditionLayout"]!.GetValue<string>().Should().Be("pre-paginated");
        page["isFixedFormat"]!.GetValue<bool>().Should().BeTrue();
        page["referencedItems"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Equal("other.xhtml");
        items[1]!["isFixedFormat"].Should().BeNull();
    }

    [Fact]
    public void Generate_ClosesTheWriterOnlyWhenAsked()
    {
        // Arrange
        TrackingWriter kept = new();
        TrackingWriter closed = new();
        CheckingReport keep = Make(new CheckingReport(kept, Book, closeWriter: false));
        CheckingReport close = Make(new CheckingReport(closed, Book, closeWriter: true));

        // Act
        keep.Generate();
        close.Generate();

        // Assert
        kept.IsDisposed.Should().BeFalse();
        closed.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void XmlReport_SaysNotWellFormed_AndListsEachLocationAsAMessage()
    {
        // Arrange
        StringWriter text = new();
        XmlReport sut = Make(new XmlReport(text, Book, "1.2.3", closeWriter: false));
        sut.Info(null, FeatureKind.FormatName, "application/epub+zip");
        sut.Info(null, FeatureKind.DcTitle, "A Title");
        sut.Message(MessageId.RSC_005, new EpubLocation("a.xhtml", 3, 4), "boom");
        sut.Message(MessageId.RSC_005, new EpubLocation("b.xhtml"), "boom");

        // Act
        sut.Generate();

        // Assert
        XNamespace ns = JhoveNamespace;
        XDocument document = XDocument.Parse(text.ToString());
        document.Root!.Name.Should().Be(ns + "jhove");
        document.Root.Attribute("release")!.Value.Should().Be("1.2.3");
        document.Descendants(ns + "status").Single().Value.Should().Be("Not well-formed");
        string[] messages = document.Descendants(ns + "message").Select(m => m.Value).ToArray();
        messages.Should().HaveCount(2);
        messages.Should().Contain(v => v.EndsWith(", a.xhtml (3-4)", StringComparison.Ordinal));
        messages.Should().Contain(v => v.EndsWith(", b.xhtml", StringComparison.Ordinal));
        document.Descendants(ns + "message").Select(m => m.Attribute("severity")!.Value).Should().OnlyContain(v => v == "error");
        document.Descendants(ns + "property").Any(p => p.Element(ns + "name")!.Value == "Info").Should().BeTrue();
    }

    [Fact]
    public void XmlReport_OfAnEmptyRun_IsWellFormed_AndNamesTheFormatAsBinaryWhenUnknown()
    {
        // Arrange
        StringWriter text = new();
        XmlReport sut = Make(new XmlReport(text, Book, null, closeWriter: false));

        // Act
        sut.Generate();

        // Assert
        XNamespace ns = JhoveNamespace;
        XDocument document = XDocument.Parse(text.ToString());
        document.Descendants(ns + "status").Single().Value.Should().Be("Well-formed");
        document.Descendants(ns + "format").Single().Value.Should().Be("application/octet-stream");
        document.Descendants(ns + "messages").Should().BeEmpty();
    }

    [Fact]
    public void XmlReport_WritesControlCharactersAsHex()
    {
        // Arrange
        StringWriter text = new();
        XmlReport sut = Make(new XmlReport(text, Book, null, closeWriter: false));
        sut.Info(null, FeatureKind.DcTitle, "a\u0001b");

        // Act
        sut.Generate();

        // Assert
        text.ToString().Should().Contain("a0x1b");
    }

    [Fact]
    public void XmpReport_DescribesThePublication_AndTheMessagesAsPremisOutcomes()
    {
        // Arrange
        StringWriter text = new();
        XmpReport sut = Make(new XmpReport(text, Book, "1.2.3", closeWriter: false));
        sut.Info(null, FeatureKind.FormatName, "application/epub+zip");
        sut.Info(null, FeatureKind.FormatVersion, "3.4");
        sut.Info(null, FeatureKind.UniqueIdent, "urn:id");
        sut.Info(null, FeatureKind.DcTitle, "A Title");
        sut.Info(null, FeatureKind.FontReference, "open sans,bold,italic");
        sut.Message(MessageId.PKG_010, new EpubLocation("a b.xhtml"), "x");

        // Act
        sut.Generate();

        // Assert
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace premis = PremisNamespace;
        XNamespace rdf = RdfNamespace;
        XNamespace font = "http://ns.adobe.com/xap/1.0/sType/Font#";
        XDocument document = XDocument.Parse(text.ToString());
        XElement description = document.Descendants(rdf + "Description").Single();
        description.Attribute(dc + "format")!.Value.Should().Be("application/epub+zip;version=3.4");
        description.Attribute(dc + "identifier")!.Value.Should().Be("urn:id");
        document.Descendants(rdf + "li").Select(li => li.Attribute(font + "fontFace")?.Value).Where(v => v is not null)
            .Should().Equal("Bold Italic");
        document.Descendants(premis + "hasEventOutcome").Single().Value.Should().StartWith("PKG-010, WARN, ");
        document.Descendants(premis + "hasEventDetail").Single().Value.Should().Be("Well-formed");
        document.Descendants(rdf + "li").Any(li => li.Attribute(premis + "hasSignificantPropertiesType")?.Value == "hasAllFontsEmbedded"
            && li.Attribute(premis + "hasSignificantPropertiesValue")!.Value == "false").Should().BeTrue();
    }

    [Fact]
    public void XmpReport_ShortensALongListOfReferences()
    {
        // Arrange
        StringWriter text = new();
        XmpReport sut = Make(new XmpReport(text, Book, null, closeWriter: false));
        Fixture fixture = new();
        for (int i = 0; i < 55; i++)
        {
            sut.Info(null, FeatureKind.Reference, fixture.Create<string>());
        }

        // Act
        sut.Generate();

        // Assert
        XNamespace premis = PremisNamespace;
        XNamespace rdf = RdfNamespace;
        XDocument document = XDocument.Parse(text.ToString());
        string[] references = document.Descendants(rdf + "li")
            .Where(li => li.Attribute(premis + "hasSignificantPropertiesType")?.Value == "reference")
            .Select(li => li.Attribute(premis + "hasSignificantPropertiesValue")!.Value).ToArray();
        references.Should().HaveCount(51);
        references[^1].Should().Be("5 more references");
    }

    private sealed class TrackingWriter : StringWriter
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
