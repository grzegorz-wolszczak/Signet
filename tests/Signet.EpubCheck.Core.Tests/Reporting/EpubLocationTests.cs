using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Reporting;

public sealed class EpubLocationTests
{
    [Theory, AutoData]
    public void Constructor_WithoutPosition_StoresMinusOneForLineAndColumn(string path)
    {
        // Act
        EpubLocation location = new(path);

        // Assert
        location.Path.Should().Be(path);
        location.Line.Should().Be(-1);
        location.Column.Should().Be(-1);
        location.Context.Should().BeNull();
    }

    [Theory, AutoData]
    public void Constructor_WithNegativePosition_StoresMinusOne(string path)
    {
        // Act
        EpubLocation location = new(path, -7, -3);

        // Assert
        location.Line.Should().Be(-1);
        location.Column.Should().Be(-1);
    }

    [Theory, AutoData]
    public void At_And_WithContext_KeepTheRest(string path, string context)
    {
        // Arrange
        EpubLocation location = new(path);

        // Act
        EpubLocation moved = location.At(4, 9).WithContext(context);

        // Assert
        moved.Should().Be(new EpubLocation(path, 4, 9, context));
        location.Line.Should().Be(-1);
    }

    [Theory, AutoData]
    public void ToString_PrintsPathLineAndColumn(string path)
    {
        // Act & Assert
        new EpubLocation(path, 12, 34).ToString().Should().Be(path + "[12,34]");
        new EpubLocation(path).ToString().Should().Be(path + "[-1,-1]");
    }

    [Fact]
    public void CompareTo_OrdersByPathThenLineThenColumnThenContext()
    {
        // Arrange
        EpubLocation[] inOrder =
        [
            new("a.xhtml"),
            new("a.xhtml", 1, 1),
            new("a.xhtml", 1, 2),
            new("a.xhtml", 2, 1),
            new("a.xhtml", 2, 1, "x"),
            new("a.xhtml", 2, 1, "y"),
            new("b.xhtml"),
        ];

        // Act & Assert
        for (int i = 0; i < inOrder.Length - 1; i++)
        {
            inOrder[i].CompareTo(inOrder[i + 1]).Should().BeNegative();
            inOrder[i + 1].CompareTo(inOrder[i]).Should().BePositive();
            (inOrder[i] < inOrder[i + 1]).Should().BeTrue();
            (inOrder[i] <= inOrder[i + 1]).Should().BeTrue();
            (inOrder[i + 1] > inOrder[i]).Should().BeTrue();
            (inOrder[i + 1] >= inOrder[i]).Should().BeTrue();
        }
    }

    [Theory, AutoData]
    public void CompareTo_OfEqualLocations_IsZero(string path, string context)
    {
        // Arrange
        EpubLocation first = new(path, 3, 4, context);
        EpubLocation second = new(path, 3, 4, context);

        // Act & Assert
        first.CompareTo(second).Should().Be(0);
        first.Should().Be(second);
    }
}
