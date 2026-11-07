using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Xml;

/// <summary>What the XML reader does where XmlReader and Xerces differ: XML 1.1 documents and the wording of some well-formedness errors.</summary>
public sealed class XmlDeclarationAndErrorTextTests
{
    [Theory]
    [InlineData("<?xml version=\"1.1\"?><a/>", "1.1")]
    [InlineData("<?xml version='1.1' encoding='utf-8'?><a/>", "1.1")]
    [InlineData("<?xml  version = \"1.1\" ?><a/>", "1.1")]
    [InlineData("<?xml version=\"1.0\"?><a/>", "1.0")]
    [InlineData("<a/>", "1.0")]
    public void Parse_DeclaredVersion_IsToldByTheLocator(string document, string expected)
    {
        // Arrange
        VersionRecorder recorder = new();

        // Act
        new XmlEventReader { ContentHandler = recorder }.Parse(InputSource.FromString(null, document));

        // Assert
        recorder.Version.Should().Be(expected);
        recorder.Elements.Should().Equal("a");
    }

    [Fact]
    public void Parse_Utf8ByteOrderMarkBeforeAnXml11Declaration_IsStillRecognised()
    {
        // Arrange
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.ASCII.GetBytes("<?xml version=\"1.1\"?><a/>")];
        VersionRecorder recorder = new();

        // Act
        new XmlEventReader { ContentHandler = recorder }.Parse(new InputSource(null, () => new System.IO.MemoryStream(bytes)));

        // Assert
        recorder.Version.Should().Be("1.1");
    }

    [Theory]
    [InlineData("<a>&foo;</a>", "The entity \"foo\" was referenced, but not declared.")]
    [InlineData("<a>&amp </a>", "The reference to entity \"amp\" must end with the ';' delimiter.")]
    [InlineData("<a>\n  text &nbsp more</a>", "The reference to entity \"nbsp\" must end with the ';' delimiter.")]
    public void Parse_EntityErrors_AreWordedAsXercesDoes(string document, string expected)
    {
        // Act
        SaxParseException error = FluentActions.Invoking(() => new XmlEventReader { ContentHandler = new VersionRecorder() }.Parse(InputSource.FromString(null, document)))
            .Should().Throw<SaxParseException>().Which;

        // Assert
        error.Message.Should().Be(expected);
    }

    [Fact]
    public void Parse_OtherErrors_KeepTheTextOfTheReader()
    {
        // Act
        SaxParseException error = FluentActions.Invoking(() => new XmlEventReader { ContentHandler = new VersionRecorder() }.Parse(InputSource.FromString(null, "<a><b></a>")))
            .Should().Throw<SaxParseException>().Which;

        // Assert
        error.Message.Should().Contain("b");
        error.LineNumber.Should().Be(1);
    }

    private sealed class VersionRecorder : IContentHandler
    {
        private ILocator? _locator;

        public string? Version => (_locator as ILocator2)?.XmlVersion;

        public List<string> Elements { get; } = [];

        public void SetDocumentLocator(ILocator locator) => _locator = locator;

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

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) => Elements.Add(localName);

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
