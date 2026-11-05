using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Vocab;

public sealed class VocabularyParserTests
{
    [Fact]
    public void ParsePrefixMappings_ValidMappings_ReturnsPrefixesAndUris()
    {
        // Arrange
        CollectingReport report = new();
        EpubLocation location = new("content.opf");

        // Act
        IReadOnlyDictionary<string, string> result = VocabularyParser.ParsePrefixMappings("foo: http://example.org/foo# bar: http://example.org/bar#", report, location);

        // Assert
        result.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["foo"] = "http://example.org/foo#",
            ["bar"] = "http://example.org/bar#",
        });
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ParsePropertyList_DeclaredVocabulary_ReturnsResolvedProperty()
    {
        // Arrange
        CollectingReport report = new();
        EpubLocation location = new("content.opf");
        IReadOnlyDictionary<string, IVocabulary> vocabularies = new Dictionary<string, IVocabulary>
        {
            ["foo"] = new UncheckedVocabulary("http://example.org/foo#", "foo"),
        };

        // Act
        IReadOnlySet<VocabProperty> result = VocabularyParser.ParsePropertyList("foo:bar", vocabularies, report, location);

        // Assert
        result.Should().ContainSingle().Which.FullName.Should().Be("http://example.org/foo#bar");
        report.Messages.Should().BeEmpty();
    }
}
