using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Xml;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Xml;

/// <summary>The XML parser of EPUBCheck: encodings, errors, the preprocessing of EPUB 3 XHTML, and the handlers' view of a document.</summary>
public sealed class XmlParserTests
{
    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>", "UTF-8")]
    [InlineData("<?xml version='1.0' encoding='iso-8859-1'?><a/>", "ISO-8859-1")]
    [InlineData("<a/>", null)]
    [InlineData("<?xml version=\"1.0\"?><a/>", null)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8", null)]
    [InlineData("<?xml encoding=UTF-8?><a/>", null)]
    [InlineData("<a", null)]
    public void SniffEncoding_Declaration_IsUpperCased(string document, string? expected)
    {
        // Act
        string? encoding = XMLEncodingSniffer.SniffEncoding(Encoding.ASCII.GetBytes(document));

        // Assert
        encoding.Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0xFE, 0xFF, 0, 0x3C }, "UTF-16")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x3C, 0 }, "UTF-16")]
    [InlineData(new byte[] { 0, 0x3C, 0, 0x3F }, "UTF-16")]
    [InlineData(new byte[] { 0, 0, 0xFE, 0xFF }, "UCS-4")]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x3C }, "UTF-8")]
    [InlineData(new byte[] { 0x4C, 0x6F, 0xA7, 0x94 }, "EBCDIC")]
    public void SniffEncoding_ByteOrderMarkOrPattern_IsRecognized(byte[] bytes, string expected)
    {
        // Act & Assert
        XMLEncodingSniffer.SniffEncoding(bytes).Should().Be(expected);
    }

    [Fact]
    public void Process_WellFormedDocument_GivesTheHandlersItsElements()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse("<?xml version=\"1.0\"?><a xmlns=\"urn:a\"><b x=\"1\"/></a>", recorder);

        // Assert
        recorder.Elements.Should().Equal("urn:a:a", "urn:a:b");
        result.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Process_Utf8Bom_IsSkipped()
    {
        // Arrange
        byte[] bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes("<a/>")];
        Recorder recorder = new();

        // Act
        ParseResult result = Parse(bytes, recorder);

        // Assert
        recorder.Elements.Should().Equal(":a");
        result.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Process_NotWellFormed_ReportsRsc016AtTheError()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse("<a>\n<b></a>", recorder);

        // Assert
        result.Messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_016 && m.Location.Line == 2);
    }

    [Fact]
    public void Process_NotWellFormedAndNotReporting_StillReportsRsc016Once()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse("<a>", recorder, reporting: false);

        // Assert
        result.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.RSC_016);
    }

    [Fact]
    public void Process_Utf16InXhtml_ReportsHtm058()
    {
        // Arrange
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\"/>")).ToArray();

        Recorder recorder = new();

        // Act
        ParseResult result = Parse(bytes, recorder, mimeType: "application/xhtml+xml");

        // Assert
        result.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.HTM_058);
    }

    [Fact]
    public void Process_Utf16InOtherXml_ReportsRsc027()
    {
        // Arrange
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<a/>")).ToArray();

        Recorder recorder = new();

        // Act
        ParseResult result = Parse(bytes, recorder);

        // Assert
        result.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.RSC_027);
    }

    [Fact]
    public void Process_OtherEncoding_ReportsRsc028WithIt()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><a/>", recorder);

        // Assert
        result.Messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_028 && (string)m.Arguments[0]! == "ISO-8859-1");
    }

    [Fact]
    public void Process_ResourceThatCannotBeRead_ReportsPkg008()
    {
        // Arrange
        CollectingReport report = new();
        ValidationContext context = new() { Url = Url.Parse("file:///missing.xml"), Report = report, ResourceProvider = new Provider(null) };

        // Act
        new XmlParser(context).Process();

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.PKG_008);
    }

    [Fact]
    public void Process_ElementsOfAnEpub3XhtmlDocument_HaveTheirCustomPartsPreprocessed()
    {
        // Arrange
        string document = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><my-element data-ok=\"1\" data-Bad=\"2\" CLASS=\"x\" hidden=\"TRUE\"/></html>";
        Recorder recorder = new();

        // Act
        ParseResult result = Parse(document, recorder, mimeType: "application/xhtml+xml", version: EpubVersion.Version3);

        // Assert
        recorder.Elements.Should().Equal("http://www.w3.org/1999/xhtml:html", "http://n.validator.nu/custom-elements/:my-element");
        recorder.Attributes.Should().Equal("CLASS=x", "hidden=true");
        result.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.HTM_061);
    }

    [Fact]
    public void Process_AttributeOfAnIdpfNamespace_ReportsHtm054AndIsRemoved()
    {
        // Arrange
        string document = "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:x=\"http://www.idpf.org/custom\" x:a=\"1\"/>";
        Recorder recorder = new();

        // Act
        ParseResult result = Parse(document, recorder, mimeType: "application/xhtml+xml", version: EpubVersion.Version3);

        // Assert
        recorder.Attributes.Should().BeEmpty();
        result.Messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.HTM_054 && (string)m.Arguments[1]! == "idpf.org");
    }

    [Theory]
    [InlineData(EpubVersion.Version2, "opf", "<!DOCTYPE package PUBLIC \"-//X//EN\" \"x.dtd\"><package/>", MessageId.HTM_009)]
    [InlineData(EpubVersion.Version3, "application/xhtml+xml", "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0//EN\" \"x.dtd\"><html/>", MessageId.HTM_004)]
    [InlineData(EpubVersion.Version3, "image/svg+xml", "<!DOCTYPE svg PUBLIC \"-//X//EN\" \"x.dtd\"><svg/>", MessageId.OPF_073)]
    [InlineData(EpubVersion.Version3, "application/xml", "<!DOCTYPE a [<!ENTITY e SYSTEM \"e.txt\">]><a/>", MessageId.HTM_003)]
    [InlineData(EpubVersion.Version2, "application/xhtml+xml", "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0//EN\" \"x.dtd\"><html/>", MessageId.HTM_004)]
    public void Process_DoctypeThatTheVersionDoesNotAllow_IsReported(EpubVersion version, string mimeType, string document, MessageId expected)
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse(document, recorder, mimeType: mimeType, version: version);

        // Assert
        result.Messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Theory]
    [InlineData(EpubVersion.Version3, "application/xhtml+xml", "<!DOCTYPE html><html/>")]
    [InlineData(EpubVersion.Version3, "application/xhtml+xml", "<!DOCTYPE html SYSTEM \"about:legacy-compat\"><html/>")]
    [InlineData(EpubVersion.Version3, "image/svg+xml", "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg/>")]
    [InlineData(EpubVersion.Version2, "opf", "<!DOCTYPE package PUBLIC \"+//ISBN 0-9673008-1-9//DTD OEB 1.2 Package//EN\" \"http://openebook.org/dtds/oeb-1.2/oebpkg12.dtd\"><package/>")]
    public void Process_DoctypeThatTheVersionAllows_IsNotReported(EpubVersion version, string mimeType, string document)
    {
        // Arrange
        Recorder recorder = new();

        // Act
        ParseResult result = Parse(document, recorder, mimeType: mimeType, version: version);

        // Assert
        result.Messages.Should().BeEmpty();
    }

    private static ParseResult Parse(string document, Recorder recorder, bool reporting = true, string mimeType = "application/xml",
        EpubVersion version = EpubVersion.Version3) =>
        Parse(Encoding.UTF8.GetBytes(document), recorder, reporting, mimeType, version);

    private static ParseResult Parse(byte[] bytes, Recorder recorder, bool reporting = true, string mimeType = "application/xml",
        EpubVersion version = EpubVersion.Version3)
    {
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        ValidationContext context = new()
        {
            Url = Url.Parse("file:///doc.xml"),
            Report = report,
            MimeType = mimeType,
            Version = version,
            ResourceProvider = new Provider(bytes),
        };
        XmlParser parser = new(context) { Reporting = reporting };
        parser.AddContentHandler(recorder);
        parser.Process();
        return new ParseResult([.. report.Messages]);
    }

    private sealed record ParseResult(List<ReportedMessage> Messages);

    private sealed class Provider(byte[]? bytes) : IResourceProvider
    {
        public Stream OpenStream(Url url) => bytes is null ? throw new IOException("missing") : new MemoryStream(bytes);
    }

    private sealed class Recorder : IContentHandler
    {
        public List<string> Elements { get; } = [];

        public List<string> Attributes { get; } = [];

        public void SetDocumentLocator(ILocator locator)
        {
        }

        public void StartDocument()
        {
        }

        public void EndDocument()
        {
        }

        public void StartPrefixMapping(string prefix, string uri)
        {
        }

        public void EndPrefixMapping(string prefix)
        {
        }

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
        {
            Elements.Add(namespaceUri + ":" + localName);
            for (int i = 0; i < attributes.Length; i++)
            {
                Attributes.Add(attributes.GetLocalName(i) + "=" + attributes.GetValue(i));
            }
        }

        public void EndElement(string namespaceUri, string localName, string qName)
        {
        }

        public void Characters(string text)
        {
        }

        public void ProcessingInstruction(string target, string data)
        {
        }
    }
}
