using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class MessageIdsTests
{
    [Fact]
    public void All_HoldsEveryIdOfUpstream5_4_0()
    {
        // Act & Assert
        MessageIds.All.Should().HaveCount(323);
    }

    [Fact]
    public void ToText_GivesADistinctTextForEveryId_AndTryParseGivesTheIdBack()
    {
        // Act
        string[] texts = MessageIds.All.Select(id => id.ToText()).ToArray();

        // Assert
        texts.Should().OnlyHaveUniqueItems();
        foreach (MessageId id in MessageIds.All)
        {
            MessageIds.TryParse(id.ToText(), out MessageId parsed).Should().BeTrue();
            parsed.Should().Be(id);
        }
    }

    [Theory]
    [InlineData(MessageId.INF_001, "INF-001")]
    [InlineData(MessageId.OPF_014, "OPF-014")]
    [InlineData(MessageId.OPF_004a, "OPF-004a")]
    [InlineData(MessageId.RSC_005, "RSC-005")]
    [InlineData(MessageId.PKG_025, "PKG-025")]
    public void ToText_PrintsTheIdWithAHyphen(MessageId id, string text)
    {
        // Act & Assert
        id.ToText().Should().Be(text);
    }

    [Fact]
    public void ToText_PrintsEveryIdWithAHyphen_EvenTheOnesUpstreamPrintsWithAnUnderscore()
    {
        // Act
        string[] texts = MessageIds.All.Select(id => id.ToText()).ToArray();

        // Assert
        texts.Should().OnlyContain(t => !t.Contains('_', StringComparison.Ordinal));
        texts.Should().OnlyContain(t => System.Text.RegularExpressions.Regex.IsMatch(t, "^[A-Z]{3}-[0-9]{3}[a-z]?$"));
        MessageId.HTM_053.ToText().Should().Be("HTM-053");
        MessageId.MED_018.ToText().Should().Be("MED-018");
    }

    [Theory]
    [InlineData("HTM_053", MessageId.HTM_053)]
    [InlineData("HTM_060a", MessageId.HTM_060a)]
    [InlineData("MED_006", MessageId.MED_006)]
    [InlineData("OPF_014", MessageId.OPF_014)]
    public void TryParse_AlsoAcceptsTheUnderscoreSpellingOfUpstream(string text, MessageId expected)
    {
        // Act
        bool parsed = MessageIds.TryParse(text, out MessageId id);

        // Assert
        parsed.Should().BeTrue();
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData("opf-014")]
    [InlineData("OPF 014")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("XXX-999")]
    public void TryParse_OfAnythingButTheExactText_Fails(string? text)
    {
        // Act
        bool parsed = MessageIds.TryParse(text, out _);

        // Assert
        parsed.Should().BeFalse();
    }

    [Fact]
    public void HasLocalizedArgument_IsTrueForTheTwoIdsUpstreamMarks()
    {
        // Act
        MessageId[] marked = MessageIds.All.Where(id => id.HasLocalizedArgument()).ToArray();

        // Assert
        marked.Should().BeEquivalentTo(new[] { MessageId.OBS_001, MessageId.PKG_007 });
    }

    [Theory, AutoData]
    public void ToText_OfAnUnknownValue_Throws(int offset)
    {
        // Arrange
        MessageId unknown = (MessageId)(MessageIds.All.Count + 1 + Math.Abs(offset % 1000));

        // Act & Assert
        FluentActions.Invoking(() => unknown.ToText()).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => DefaultSeverities.Get(unknown)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
