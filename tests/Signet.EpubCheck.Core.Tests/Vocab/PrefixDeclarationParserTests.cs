using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Vocab;

/// <summary>The cases of upstream's <c>PrefixParsingTest</c> (EPUBCheck 5.4.0).</summary>
public sealed class PrefixDeclarationParserTests
{
    private const string FooUri = "http://example.org/foo#";
    private const string BarUri = "http://example.org/bar#";
    private static readonly KeyValuePair<string, string>[] s_fooBar = [new("foo", FooUri), new("bar", BarUri)];

    [Theory]
    [InlineData("foo: http://example.org/foo# bar: http://example.org/bar#", new MessageId[0], new MessageId[0])]
    [InlineData("  foo:   http://example.org/foo# \n\t\r bar:   http://example.org/bar#   ", new MessageId[0], new[] { MessageId.OPF_004, MessageId.OPF_004 })]
    [InlineData("foo:\t http://example.org/foo#   bar: http://example.org/bar#", new MessageId[0], new[] { MessageId.OPF_004e, MessageId.OPF_004f })]
    [InlineData("foo: http://example.org/foo# bar: http://example.org/bar# baz", new[] { MessageId.OPF_005 }, new MessageId[0])]
    [InlineData("foo: http://example.org/foo# bar: http://example.org/bar# baz:", new[] { MessageId.OPF_005 }, new MessageId[0])]
    [InlineData("foo: http://example.org/foo# bar: http://example.org/bar# baz: ", new[] { MessageId.OPF_005 }, new MessageId[0])]
    [InlineData("bad: [bad] foo: http://example.org/foo# bar: http://example.org/bar#", new[] { MessageId.OPF_006 }, new MessageId[0])]
    public void ParsePrefixMappings_FooAndBar_AreMapped(string value, MessageId[] errors, MessageId[] warnings)
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        IReadOnlyList<KeyValuePair<string, string>> mappings = PrefixDeclarationParser.ParsePrefixMappings(value, report, VocabTestSupport.Location);

        // Assert
        mappings.Should().Equal(s_fooBar);
        report.Ids(Severity.Error).Should().Equal(errors);
        report.Ids(Severity.Warning).Should().Equal(warnings);
    }

    [Theory]
    [InlineData("", new MessageId[0])]
    [InlineData(null, new MessageId[0])]
    [InlineData(": http://example.org/foo# : http://example.org/bar#", new[] { MessageId.OPF_004a, MessageId.OPF_004a })]
    [InlineData("123: http://example.org/foo#", new[] { MessageId.OPF_004b })]
    [InlineData("foo http://example.org/foo# bar  : http://example.org/bar#", new[] { MessageId.OPF_004c, MessageId.OPF_004c })]
    [InlineData("foo:http://example.org/foo#", new[] { MessageId.OPF_004d })]
    public void ParsePrefixMappings_NothingValid_IsEmpty(string? value, MessageId[] errors)
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        IReadOnlyList<KeyValuePair<string, string>> mappings = PrefixDeclarationParser.ParsePrefixMappings(value, report, VocabTestSupport.Location);

        // Assert
        mappings.Should().BeEmpty();
        report.Ids(Severity.Error).Should().Equal(errors);
        report.Ids(Severity.Warning).Should().BeEmpty();
    }

    [Fact]
    public void ParsePrefixMappings_PrefixDeclaredTwice_KeepsTheFirstUri()
    {
        // Arrange (upstream's ImmutableMap.Builder throws on the duplicate: listed in UPSTREAM.md)
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        IReadOnlyList<KeyValuePair<string, string>> mappings =
            PrefixDeclarationParser.ParsePrefixMappings("foo: http://example.org/foo# foo: http://example.org/bar#", report, VocabTestSupport.Location);

        // Assert
        mappings.Should().Equal([new KeyValuePair<string, string>("foo", FooUri)]);
    }
}
