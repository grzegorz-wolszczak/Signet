using System;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Confirms that the whole set of test tools (xUnit.v3, AwesomeAssertions, Moq, AutoFixture)
/// is configured correctly.
/// </summary>
public sealed class ToolingSmokeTests
{
    [Theory]
    [AutoData]
    public void AutoFixture_supplies_anonymous_values(string text, int number)
    {
        text.Should().NotBeNullOrEmpty();
        number.Should().NotBe(0);
    }

    [Fact]
    public void Moq_creates_a_working_test_double()
    {
        Mock<IComparable<int>> comparable = new();
        comparable.Setup(c => c.CompareTo(It.IsAny<int>())).Returns(42);

        comparable.Object.CompareTo(7).Should().Be(42);
        comparable.Verify(c => c.CompareTo(7), Times.Once);
    }
}
