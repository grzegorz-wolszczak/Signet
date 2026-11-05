using System;
using System.Globalization;
using System.IO;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class MessageDictionaryDumperTests
{
    [Fact]
    public void Dump_WritesAHeaderAndALinePerMessage_EndedByLineFeeds()
    {
        // Arrange
        MessageDictionaryDumper dumper = new(new LocalizedMessageDictionary(CultureInfo.GetCultureInfo("en")));
        using StringWriter writer = new();

        // Act
        dumper.Dump(writer);

        // Assert
        string text = writer.ToString();
        text.Should().NotContain("\r");
        string[] lines = text.Split('\n');
        lines.Should().HaveCount(1 + 323 + 1);
        lines[0].Should().Be("ID\tSeverity\tMessage\tSuggestion");
        lines[^1].Should().BeEmpty();
        lines[1].Should().StartWith("INF-001\tINFO\t");
        text.Should().Contain("\nOPF-014\tERROR\tThe property \"%1$s\" should be declared in the OPF file.\t\n");
        text.Should().Contain("\nOPF-035\tWARNING\tMedia type \"text/html\" is not appropriate for XHTML/OPS.\tUse \"application/xhtml+xml\" instead.\n");
        text.Should().Contain("\nHTM-053\tSUPPRESSED\t\t\n");
    }

    [Fact]
    public void Constructor_WithoutADictionary_Throws()
    {
        // Act & Assert
        FluentActions.Invoking(() => new MessageDictionaryDumper(null!)).Should().Throw<ArgumentNullException>();
    }
}
