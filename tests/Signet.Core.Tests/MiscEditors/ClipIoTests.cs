using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="ClipIo"/> and <see cref="ClipStore"/> — the XML clip library.</summary>
public sealed class ClipIoTests
{
    private static readonly Fixture Fixture = new();

    private static readonly ClipEntry[] Sample =
    {
        new(IsGroup: false, new[] { "Example Clips" }, "p", "<p>\\1</p>"),
        new(IsGroup: false, new[] { "Example Clips", "epub3" }, "section", "<section>\r\n  \\1\r\n</section>"),
        new(IsGroup: true, new[] { "Example Clips" }, "Empty", ""),
        new(IsGroup: false, Array.Empty<string>(), "Plain, with a comma", "<span class=\"x\">\\1</span>"),
        new(IsGroup: false, new[] { "Tags/XHTML" }, "<test/>", "<test/>"),
        new(IsGroup: true, Array.Empty<string>(), "a/b", ""),
    };

    [Fact]
    public void WriteXml_ThenReadXml_RoundTrips()
    {
        // Act
        IReadOnlyList<ClipEntry> parsed = ClipIo.ReadXml(ClipIo.WriteXml(Sample));

        // Assert
        parsed.Should().BeEquivalentTo(Sample, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Groups_are_nested_elements_and_names_with_a_slash_stay_one_name()
    {
        // Act
        XElement root = XDocument.Parse(ClipIo.WriteXml(Sample)).Root!;

        // Assert
        root.Name.LocalName.Should().Be("clips");
        XElement example = root.Elements("group").First();
        example.Attribute("name")!.Value.Should().Be("Example Clips");
        example.Elements("clip").Single().Attribute("name")!.Value.Should().Be("p");
        example.Elements("group").Select(g => g.Attribute("name")!.Value).Should().Equal("epub3", "Empty");
        root.Elements("group").Select(g => g.Attribute("name")!.Value).Should().Equal("Example Clips", "Tags/XHTML", "a/b");
        root.Elements("group").Single(g => g.Attribute("name")!.Value == "Tags/XHTML")
            .Element("clip")!.Attribute("name")!.Value.Should().Be("<test/>");
    }

    [Theory]
    [InlineData("<clips")]
    [InlineData("{ \"clip_entries\": [ { \"Name\": \"x\", \"Text\": \"y\" } ] }")]
    [InlineData("<other><clip name=\"x\">y</clip></other>")]
    [InlineData("")]
    [InlineData(null)]
    public void ReadXml_of_invalid_or_foreign_content_returns_empty(string? content)
    {
        // Act & Assert
        ClipIo.ReadXml(content).Should().BeEmpty();
    }

    [Fact]
    public void Store_SaveThenReloadWithNewInstance_PersistsEntries()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("clips.xml");

        // Act
        new ClipStore(path).Save(Sample);

        // Act & Assert
        new ClipStore(path).Load().Should().BeEquivalentTo(Sample, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Store_does_not_read_a_json_library()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("clips.xml");
        File.WriteAllText(path, "{ \"clip_entries\": [ { \"Name\": \"" + Fixture.Create<string>() + "\" } ] }");

        // Act & Assert
        new ClipStore(path).Load().Should().BeEmpty();
    }

    [Fact]
    public void Store_Load_MissingFile_ReturnsEmpty()
    {
        // Act & Assert
        new ClipStore(Path.Combine("nonexistent-dir", "missing.xml")).Load().Should().BeEmpty();
    }

    [Fact]
    public void Default_file_is_clips_xml()
    {
        // Act & Assert
        ClipStore.DefaultFileName.Should().Be("clips.xml");
    }
}
