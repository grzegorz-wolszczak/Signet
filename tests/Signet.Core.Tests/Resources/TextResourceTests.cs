using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="TextResource"/> — the text buffer, loading and saving.</summary>
public sealed class TextResourceTests
{
    private static (TempDir Root, string Full) Prepare(string bookPath)
    {
        TempDir root = new();
        string full = root.Combine(bookPath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return (root, full);
    }

    [Fact]
    public void InitialLoad_reads_content_from_disk_on_demand()
    {
        (TempDir root, string full) = Prepare("Styles/s.css");
        using (root)
        {
            File.WriteAllText(full, "body { color: red; }");
            TextResource resource = new(root.Path, full);

            resource.IsLoaded.Should().BeFalse();
            resource.InitialLoad();

            resource.IsLoaded.Should().BeTrue();
            resource.GetText().Should().Be("body { color: red; }");
        }
    }

    [Fact]
    public void SetText_updates_buffer_and_raises_modified()
    {
        (TempDir root, string full) = Prepare("Misc/x.txt");
        using (root)
        {
            TextResource resource = new(root.Path, full);
            int modifiedCount = 0;
            resource.Modified += (_, _) => modifiedCount++;

            resource.SetText("hello");

            resource.GetText().Should().Be("hello");
            modifiedCount.Should().Be(1);
        }
    }

    [Fact]
    public void SaveToDisk_writes_utf8_without_bom_and_lf_line_endings()
    {
        (TempDir root, string full) = Prepare("Text/a.xhtml");
        using (root)
        {
            TextResource resource = new(root.Path, full);
            resource.SetText("line1\r\nline2\rline3\né");

            resource.SaveToDisk();

            byte[] bytes = File.ReadAllBytes(full);
            bytes[0].Should().NotBe((byte)0xEF);
            bytes.Should().NotContain((byte)'\r');
            Encoding.UTF8.GetString(bytes).Should().Be("line1\nline2\nline3\né");
        }
    }

    [Fact]
    public void SaveToDisk_is_a_no_op_when_nothing_was_loaded()
    {
        (TempDir root, string full) = Prepare("Text/a.xhtml");
        using (root)
        {
            TextResource resource = new(root.Path, full);
            resource.SaveToDisk();

            File.Exists(full).Should().BeFalse();
        }
    }

    [Fact]
    public void Round_trips_content_through_disk()
    {
        (TempDir root, string full) = Prepare("Text/a.xhtml");
        using (root)
        {
            const string content = "<p>Zażółć gęślą jaźń — —  </p>\n";
            TextResource writer = new(root.Path, full);
            writer.SetText(content);
            writer.SaveToDisk();

            TextResource reader = new(root.Path, full);
            reader.InitialLoad();
            reader.GetText().Should().Be(content);
        }
    }
}
