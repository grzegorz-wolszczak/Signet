using System.IO;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="BookStateFile"/> — the XML state file of a working folder.</summary>
public sealed class BookStateFileTests
{
    private static readonly Fixture Fixture = new();

    [Fact]
    public void The_font_obfuscation_map_is_read_from_the_xml_state_file()
    {
        // Arrange
        using TempDir dir = new();
        string bookPath = "OEBPS/Fonts/" + Fixture.Create<string>() + ".ttf";
        File.WriteAllText(Path.Combine(dir.Path, BookStateFile.FileName), $"""
            <signet_state>
              <font book_path="{bookPath}" obfuscation="http://www.idpf.org/2008/embedding" />
              <font book_path="" obfuscation="ignored" />
            </signet_state>
            """);

        // Act & Assert
        BookStateFile.ReadFontObfuscation(dir.Path).Should().ContainSingle()
            .Which.Should().Be(new System.Collections.Generic.KeyValuePair<string, string>(bookPath, "http://www.idpf.org/2008/embedding"));
    }

    [Fact]
    public void A_legacy_json_state_file_is_not_read_but_is_a_state_file()
    {
        // Arrange
        using TempDir dir = new();
        File.WriteAllText(
            Path.Combine(dir.Path, BookStateFile.LegacyFileName),
            "{\"FontObfuscation\":{\"OEBPS/Fonts/a.ttf\":\"http://www.idpf.org/2008/embedding\"}}");

        // Act & Assert
        BookStateFile.ReadFontObfuscation(dir.Path).Should().BeEmpty();

        // Act & Assert
        BookStateFile.IsStateFile(BookStateFile.LegacyFileName).Should().BeTrue();

        // Act & Assert
        BookStateFile.IsStateFile(BookStateFile.FileName).Should().BeTrue();

        // Act & Assert
        BookStateFile.IsStateFile("OEBPS/content.opf").Should().BeFalse();
    }

    [Fact]
    public void The_state_file_is_signet_state_xml()
    {
        // Act & Assert
        BookStateFile.FileName.Should().Be(".signet-state.xml");
    }
}
