using System.IO;
using System.Xml.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>Tests of <see cref="XmlConfig"/> — lenient reading, atomic writing, text that survives a round trip.</summary>
public sealed class XmlConfigTests
{
    private static readonly Fixture Fixture = new();

    private static string RoundTrip(string value)
    {
        XDocument document = new(new XElement("root", new XElement("value", value), new XAttribute("attr", value)));
        XDocument parsed = XmlConfig.TryParse(XmlConfig.ToText(document))!;
        parsed.Root!.Attribute("attr")!.Value.Should().Be(value, "attribute values keep their characters too");
        return parsed.Root.Element("value")!.Value;
    }

    [Theory]
    [InlineData("line1\r\nline2\rline3\nline4")]
    [InlineData("   ")]
    [InlineData("\n")]
    [InlineData("  leading and trailing  ")]
    [InlineData("<test/> & \"quotes\" 'apostrophes'")]
    [InlineData("tab\there")]
    public void Text_round_trips_unchanged(string value)
    {
        // Act & Assert
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void A_control_character_does_not_make_writing_or_reading_fail()
    {
        // Arrange
        string value = Fixture.Create<string>() + "\u0001";

        // Act & Assert
        RoundTrip(value).Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<root>")]
    [InlineData("{ \"json\": true }")]
    public void Malformed_or_empty_text_yields_null(string? text)
    {
        // Act & Assert
        XmlConfig.TryParse(text).Should().BeNull();
    }

    [Fact]
    public void A_DTD_is_rejected()
    {
        // Act & Assert
        XmlConfig.TryParse("<!DOCTYPE root [<!ENTITY e \"x\">]><root>&e;</root>").Should().BeNull();
    }

    [Fact]
    public void Save_then_load_round_trips_and_leaves_no_temporary_file()
    {
        // Arrange
        using TempDir dir = new();
        string path = Path.Combine(dir.Combine("sub"), "config.xml");
        string value = Fixture.Create<string>();

        // Act
        XmlConfig.Save(path, new XDocument(new XElement("root", value)));

        // Assert
        XmlConfig.TryLoad(path)!.Root!.Value.Should().Be(value);
        File.Exists(path + ".tmp").Should().BeFalse();
        File.ReadAllText(path).Should().StartWith("<?xml");
    }

    [Fact]
    public void Load_of_a_missing_file_yields_null()
    {
        // Arrange
        using TempDir dir = new();

        // Act & Assert
        XmlConfig.TryLoad(dir.Combine("missing.xml")).Should().BeNull();
    }
}
