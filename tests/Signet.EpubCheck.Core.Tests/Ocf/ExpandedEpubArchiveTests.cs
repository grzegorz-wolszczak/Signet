using System.IO;
using System.IO.Compression;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Ocf;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class ExpandedEpubArchiveTests
{
    private const string MimeType = "application/epub+zip";

    private static string MakeFolder(TempDir dir, string name = "book")
    {
        string folder = dir.Combine(name);
        Directory.CreateDirectory(Path.Combine(folder, "META-INF"));
        File.WriteAllText(Path.Combine(folder, "OPS.txt"), "content");
        File.WriteAllText(Path.Combine(folder, "META-INF", "container.xml"), "<container/>");
        File.WriteAllText(Path.Combine(folder, "mimetype"), MimeType);
        return folder;
    }

    [Fact]
    public void Create_PutsTheMimetypeFirstAndStored_ThenTheOtherFiles()
    {
        // Arrange
        using TempDir dir = new();
        string folder = MakeFolder(dir);

        // Act
        using ExpandedEpubArchive archive = ExpandedEpubArchive.Create(folder, keep: false);

        // Assert
        using ZipArchive zip = ZipFile.OpenRead(archive.EpubPath);
        zip.Entries.Select(e => e.FullName).Should().Equal("mimetype", "META-INF/container.xml", "OPS.txt");
        zip.Entries[0].CompressedLength.Should().Be(zip.Entries[0].Length);
    }

    [Fact]
    public void Create_WithKeep_LeavesTheEpubBesideTheFolder()
    {
        // Arrange
        using TempDir dir = new();
        string folder = MakeFolder(dir);
        string expected;

        // Act
        using (ExpandedEpubArchive archive = ExpandedEpubArchive.Create(folder, keep: true))
        {
            expected = archive.EpubPath;
        }

        // Assert
        expected.Should().Be(folder + ".epub");
        File.Exists(expected).Should().BeTrue();
    }

    [Fact]
    public void Dispose_WithoutKeep_DeletesTheEpub()
    {
        // Arrange
        using TempDir dir = new();
        string folder = MakeFolder(dir);
        ExpandedEpubArchive archive = ExpandedEpubArchive.Create(folder, keep: false);
        string path = archive.EpubPath;
        File.Exists(path).Should().BeTrue();

        // Act
        archive.Dispose();

        // Assert
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Create_WithoutKeep_DoesNotTouchAFileThatIsAlreadyThere()
    {
        // Arrange
        using TempDir dir = new();
        string folder = MakeFolder(dir);
        File.WriteAllText(folder + ".epub", "mine");

        // Act
        using (ExpandedEpubArchive archive = ExpandedEpubArchive.Create(folder, keep: false))
        {
            // Assert
            archive.EpubPath.Should().NotBe(folder + ".epub");
        }

        File.ReadAllText(folder + ".epub").Should().Be("mine");
    }
}
