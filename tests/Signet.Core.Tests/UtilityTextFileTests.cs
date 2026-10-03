using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>Tests of <see cref="Utility"/> for text file I/O.</summary>
public sealed class UtilityTextFileTests
{
    [Theory]
    [InlineData("a\r\nb\rc\nd", "a\nb\nc\nd")]
    [InlineData("no eol", "no eol")]
    public void ConvertLineEndingsAndNormalize_unifies_line_endings(string input, string expected)
    {
        Utility.ConvertLineEndingsAndNormalize(input).Should().Be(expected);
    }

    [Fact]
    public void WriteUnicodeTextFile_writes_utf8_without_bom()
    {
        using TempDir root = new();
        string full = root.Combine("out.txt");

        Utility.WriteUnicodeTextFile("Zażółć — €", full);

        byte[] bytes = File.ReadAllBytes(full);
        bytes[0].Should().NotBe((byte)0xEF);
        Encoding.UTF8.GetString(bytes).Should().Be("Zażółć — €");
    }

    [Fact]
    public void ReadUnicodeTextFile_detects_utf16_bom_and_normalizes()
    {
        using TempDir root = new();
        string full = root.Combine("in.txt");
        File.WriteAllBytes(full, Encoding.Unicode.GetPreamble());
        File.AppendAllText(full, "line1\r\nline2", Encoding.Unicode);

        Utility.ReadUnicodeTextFile(full).Should().Be("line1\nline2");
    }

    [Fact]
    public void Text_file_round_trips_through_write_then_read()
    {
        using TempDir root = new();
        string full = root.Combine("rt.txt");
        const string content = "między\nwierszami\n";

        Utility.WriteUnicodeTextFile(content, full);

        Utility.ReadUnicodeTextFile(full).Should().Be(content);
    }
}
