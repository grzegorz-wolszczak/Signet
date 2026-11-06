using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Xml;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Xml;

/// <summary>
/// The errors of the schemas as EPUBCheck reports them (upstream's <c>ReportingErrorHandler</c> and
/// <c>XMLParser.addValidator</c>): the mapping to message ids, and the fixtures of upstream's scenarios whose messages
/// come from the schemas alone, checked with the validators upstream's checkers use for them.
/// </summary>
public sealed class SchemaMessageTests
{
    private const string FilePath = "EPUB/content.xhtml";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");
    private static readonly string s_statementStart = Environment.NewLine + "  ";

    private readonly Fixture _fixture = new();

    [Fact]
    public void OnError_PlainMessage_IsRsc005AtTheLineAndColumnOfTheError()
    {
        // Arrange
        (CollectingReport report, ReportingErrorHandler handler) = Handler(normative: true);
        string text = _fixture.Create<string>();

        // Act
        handler.OnError(Error("  " + text + " ", line: 3, column: 7));

        // Assert
        report.Messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_005 && m.Location == new EpubLocation(FilePath, 3, 7, null) && (string)m.Arguments[0]! == text);
    }

    [Theory]
    [InlineData(true, "WARNING: Duplicate entry", MessageId.RSC_017, "Duplicate entry")]
    [InlineData(false, "WARNING: Duplicate entry", MessageId.RSC_024, "Duplicate entry")]
    [InlineData(true, "USAGE: Obsolete attribute", MessageId.RSC_036, "Obsolete attribute")]
    [InlineData(false, "USAGE: Obsolete attribute", MessageId.RSC_036, "Obsolete attribute")]
    [InlineData(false, "element \"foo\" not allowed here", MessageId.RSC_025, "element \"foo\" not allowed here")]
    [InlineData(true, "WARNING:", MessageId.RSC_017, "")]
    public void OnError_PrefixedOrInformative_GetsUpstreamsId(bool normative, string message, MessageId expectedId, string expectedText)
    {
        // Arrange
        (CollectingReport report, ReportingErrorHandler handler) = Handler(normative);

        // Act
        handler.OnError(Error(s_statementStart + message, line: 1, column: 1));

        // Assert
        report.Messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == expectedId && (string)m.Arguments[0]! == expectedText);
    }

    [Theory]
    [InlineData(true, MessageId.RSC_017)]
    [InlineData(false, MessageId.RSC_024)]
    public void OnWarning_IsRsc017OrRsc024(bool normative, MessageId expectedId)
    {
        // Arrange
        (CollectingReport report, ReportingErrorHandler handler) = Handler(normative);

        // Act
        handler.OnWarning(Error(_fixture.Create<string>(), line: 2, column: 2));

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(expectedId);
    }

    [Fact]
    public void OnFatalError_IsRsc016()
    {
        // Arrange
        (CollectingReport report, ReportingErrorHandler handler) = Handler(normative: false);

        // Act
        handler.OnFatalError(Error(_fixture.Create<string>(), line: 2, column: 2));

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.RSC_016);
    }

    [Fact]
    public void Validate_SchematronWarning_IsRsc017WithTheStatementOnly()
    {
        // Act: outdated-features.feature, "'guide' should not contain two entries of the same type pointing to the same resource".
        List<ReportedMessage> messages = Check("epub3/E-obsolete-features/files/outdated-legacy-guide-duplicates-warning.opf",
            XmlValidators.Opf30Rnc, XmlValidators.Opf30Sch, XmlValidators.Opf30CollectionDoSch, XmlValidators.Opf30CollectionDictSch,
            XmlValidators.Opf30CollectionIdxSch, XmlValidators.Opf30CollectionManifestSch, XmlValidators.Opf30CollectionPreviewSch);

        // Assert
        messages.Should().HaveCount(2).And.AllSatisfy(m =>
        {
            m.Message.Id.Should().Be(MessageId.RSC_017);
            m.Text.Should().Contain("Duplicate \"reference\" elements with the same \"type\" and \"href\" attributes");
            m.Text.Should().NotContain("WARNING");
        });
    }

    [Fact]
    public void Validate_SchematronWarningInNav_IsRsc017()
    {
        // Act: navigation-document.feature, "Report nested `ol` in `page-list` nav".
        List<ReportedMessage> messages = Check("epub3/08-navigation-document/files/nav-page-list-nested-warning.xhtml",
            XmlValidators.Nav30Rnc, XmlValidators.Xhtml30Sch, XmlValidators.Nav30Sch);

        // Assert
        messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_017 && m.Text.Contains("page-list") && m.Text.Contains("no nested sublists"));
    }

    [Theory]
    [InlineData("obsolete-img-border-0-usage.xhtml", "the 'border' attribute")]
    [InlineData("obsolete-script-charset-utf8-usage.xhtml", "the 'charset' attribute")]
    [InlineData("obsolete-style-type-css-usage.xhtml", "the 'type' attribute")]
    [InlineData("obsolete-a-name-usage.xhtml", "the 'name' attribute")]
    public void Validate_SchematronUsage_IsRsc036(string file, string expectedText)
    {
        // Act: content-documents-xhtml.feature, the obsolete but conforming attributes.
        List<ReportedMessage> messages = Check("epub3/07-content-documents/files/" + file,
            XmlValidators.Xhtml30Nvdl, XmlValidators.Svg30InformativeNvdl);

        // Assert
        messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_036 && m.Text.Contains(expectedText));
    }

    [Theory]
    [InlineData("svg-invalid-usage.xhtml", XmlValidators.Xhtml30Nvdl)]
    [InlineData("svg-invalid-usage.svg", XmlValidators.Svg30Nvdl)]
    public void Validate_ErrorAgainstTheInformativeSvgSchema_IsRsc025(string file, XmlValidators normative)
    {
        // Act: content-documents-xhtml.feature and content-documents-svg.feature, "SVG validation errors are reported as USAGE".
        List<ReportedMessage> messages = Check("epub3/07-content-documents/files/" + file, normative, XmlValidators.Svg30InformativeNvdl);

        // Assert
        messages.Should().ContainSingle().Which.Should().Match<ReportedMessage>(m =>
            m.Message.Id == MessageId.RSC_025 && m.Text.Contains("element \"foo\" not allowed here"));
    }

    [Fact]
    public void Validate_RelaxNgError_IsRsc005AtTheElement()
    {
        // Arrange
        CollectingReport report = Report();
        string document = "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\">\n  <unknown/>\n</package>";

        // Act
        Validate(report, InputSource.FromString("file:///EPUB/package.opf", document), XmlValidators.Opf30Rnc);

        // Assert
        report.Messages.Should().Contain(m => m.Message.Id == MessageId.RSC_005 && m.Location.Line == 2
            && m.Text.Contains("element \"unknown\" not allowed", StringComparison.Ordinal));
    }

    private static List<ReportedMessage> Check(string fixture, params XmlValidators[] validators)
    {
        CollectingReport report = Report();
        string path = UpstreamCorpus.Combine(fixture.Split('/'));
        Validate(report, new InputSource(new Uri(path).AbsoluteUri, () => File.OpenRead(path)), validators);
        return [.. report.Messages];
    }

    private static void Validate(CollectingReport report, InputSource source, params XmlValidators[] validators)
    {
        EpubLocation location = new(FilePath);
        List<IValidator> created = [.. validators.Select(v => v.Get().CreateValidator(report, location, s_english))];
        new XmlEventReader { ContentHandler = new Tee(created), DtdHandler = new Tee(created) }.Parse(source);
    }

    private static CollectingReport Report()
    {
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        report.SetLocale(s_english);
        return report;
    }

    private static (CollectingReport Report, ReportingErrorHandler Handler) Handler(bool normative)
    {
        CollectingReport report = Report();
        return (report, new ReportingErrorHandler(report, new EpubLocation(FilePath), normative));
    }

    private static SaxParseException Error(string message, int line, int column) =>
        new(message, "file:///" + FilePath, line, column, null);

    // Feeds the events of one document to several validators, as upstream's DelegateDefaultHandler does.
    private sealed class Tee(List<IValidator> validators) : IContentHandler, IDtdHandler
    {
        public void SetDocumentLocator(ILocator locator) => validators.ForEach(v => v.ContentHandler.SetDocumentLocator(locator));

        public void StartDocument() => validators.ForEach(v => v.ContentHandler.StartDocument());

        public void EndDocument() => validators.ForEach(v => v.ContentHandler.EndDocument());

        public void StartPrefixMapping(string prefix, string uri) => validators.ForEach(v => v.ContentHandler.StartPrefixMapping(prefix, uri));

        public void EndPrefixMapping(string prefix) => validators.ForEach(v => v.ContentHandler.EndPrefixMapping(prefix));

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) =>
            validators.ForEach(v => v.ContentHandler.StartElement(namespaceUri, localName, qName, attributes));

        public void EndElement(string namespaceUri, string localName, string qName) =>
            validators.ForEach(v => v.ContentHandler.EndElement(namespaceUri, localName, qName));

        public void Characters(string text) => validators.ForEach(v => v.ContentHandler.Characters(text));

        public void ProcessingInstruction(string target, string data) => validators.ForEach(v => v.ContentHandler.ProcessingInstruction(target, data));

        public void NotationDecl(string name, string? publicId, string? systemId) =>
            validators.ForEach(v => v.DtdHandler.NotationDecl(name, publicId, systemId));

        public void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName) =>
            validators.ForEach(v => v.DtdHandler.UnparsedEntityDecl(name, publicId, systemId, notationName));
    }
}
