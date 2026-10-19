using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.SourceUpdates;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>Tests for <see cref="HrefUpdate"/> — recomputing a single reference value.</summary>
public sealed class HrefUpdateTests
{
    [Fact]
    public void UpdateValue_ExternalScheme_RemainsUnchanged()
    {
        // Arrange
        Dictionary<string, string> updates = new() { ["OEBPS/old.jpg"] = "OEBPS/new.jpg" };

        // Act
        string result = HrefUpdate.UpdateValue(
            "https://example.com/old.jpg", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("https://example.com/old.jpg");
    }

    [Fact]
    public void UpdateValue_DataUri_RemainsUnchanged()
    {
        // Arrange
        Dictionary<string, string> updates = new();

        // Act
        string result = HrefUpdate.UpdateValue(
            "data:image/png;base64,AAAA", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("data:image/png;base64,AAAA");
    }

    [Fact]
    public void UpdateValue_PureFragment_RemainsUnchanged()
    {
        // Arrange
        Dictionary<string, string> updates = new();

        // Act
        string result = HrefUpdate.UpdateValue(
            "#section1", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("#section1");
    }

    [Fact]
    public void UpdateValue_TargetRenamed_RewritesRelativePath()
    {
        // Arrange
        Dictionary<string, string> updates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };

        // Act
        string result = HrefUpdate.UpdateValue(
            "../Images/old.jpg", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("../Images/new.jpg");
    }

    [Fact]
    public void UpdateValue_TargetRenamed_PreservesFragment()
    {
        // Arrange
        Dictionary<string, string> updates = new() { ["OEBPS/Text/ch2.xhtml"] = "OEBPS/Text/ch2-new.xhtml" };

        // Act
        string result = HrefUpdate.UpdateValue(
            "ch2.xhtml#note1", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("ch2-new.xhtml#note1");
    }

    [Fact]
    public void UpdateValue_SourceFileMoved_RecalculatesRelativePathEvenWhenTargetUnchanged()
    {
        // Arrange
        Dictionary<string, string> updates = new();

        // Act
        string result = HrefUpdate.UpdateValue(
            "../Images/cover.jpg", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/Sub/ch1.xhtml");

        // Assert
        result.Should().Be("../../Images/cover.jpg");
    }

    [Fact]
    public void UpdateValue_UnrelatedTarget_RemainsUnchanged()
    {
        // Arrange
        Dictionary<string, string> updates = new() { ["OEBPS/Images/other.jpg"] = "OEBPS/Images/other2.jpg" };

        // Act
        string result = HrefUpdate.UpdateValue(
            "../Images/keep.jpg", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("../Images/keep.jpg");
    }

    [Fact]
    public void UpdateValue_EncodedFragmentIsPreservedAsEncoded()
    {
        // Arrange
        Dictionary<string, string> updates = new() { ["OEBPS/Text/ch2.xhtml"] = "OEBPS/Text/ch2-new.xhtml" };

        // Act
        string result = HrefUpdate.UpdateValue(
            "ch2.xhtml#a%20b", updates, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml");

        // Assert
        result.Should().Be("ch2-new.xhtml#a%20b");
    }
}
