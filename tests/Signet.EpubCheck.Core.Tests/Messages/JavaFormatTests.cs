using System;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class JavaFormatTests
{
    [Theory, AutoData]
    public void Format_FillsArgumentsByPosition_AndMayRepeatOne(string first, string second)
    {
        // Act
        string text = JavaFormat.Format("a %2$s b %1$s c %1$s", first, second);

        // Assert
        text.Should().Be($"a {second} b {first} c {first}");
    }

    [Theory, AutoData]
    public void Format_FillsOrdinaryPlaceholdersInOrder(string first, string second)
    {
        // Act
        string text = JavaFormat.Format("%s and %s", first, second);

        // Assert
        text.Should().Be($"{first} and {second}");
    }

    [Fact]
    public void Format_UnderstandsPercentAndNewlineConversions()
    {
        // Act
        string text = JavaFormat.Format("100%% done%n");

        // Assert
        text.Should().Be("100% done\n");
    }

    [Fact]
    public void Format_PrintsArgumentsTheWayJavaDoes()
    {
        // Act
        string text = JavaFormat.Format("%1$s %2$s %3$s %4$s", null, true, false, 12.5);

        // Assert
        text.Should().Be("null true false 12.5");
    }

    [Theory]
    [InlineData(0, "0 errors")]
    [InlineData(1234, "1234 errors")]
    public void Format_FillsAnIntegerWithD(int count, string expected)
    {
        // Act & Assert
        JavaFormat.Format("%1$d errors", count).Should().Be(expected);
    }

    [Fact]
    public void Format_OfAStringWithD_Throws()
    {
        // Act & Assert
        FluentActions.Invoking(() => JavaFormat.Format("%1$d", "text")).Should().Throw<FormatException>();
    }

    [Fact]
    public void Format_IgnoresArgumentsThatNoPlaceholderUses()
    {
        // Act & Assert
        JavaFormat.Format("no placeholders", "unused").Should().Be("no placeholders");
    }

    [Theory]
    [InlineData("%2$s", "Format specifier '%2$s'")]
    [InlineData("%s %s", "Format specifier '%s'")]
    public void Format_WithAMissingArgument_Throws(string template, string reason)
    {
        // Act & Assert
        FluentActions.Invoking(() => JavaFormat.Format(template, "only one"))
            .Should().Throw<FormatException>().WithMessage(reason);
    }

    [Theory]
    [InlineData("%1$x")]
    [InlineData("%5s")]
    [InlineData("100%")]
    public void Format_WithAConversionItDoesNotSupport_Throws(string template)
    {
        // Act & Assert
        FluentActions.Invoking(() => JavaFormat.Format(template, 1)).Should().Throw<FormatException>();
    }
}
