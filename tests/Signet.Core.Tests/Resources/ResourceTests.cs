using System.IO;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for the <see cref="Resource"/> base class — paths, rename / move / delete.</summary>
public sealed class ResourceTests
{
    private static (TempDir Root, string FullPath) MakeFile(string bookPath, string content = "x")
    {
        TempDir root = new();
        string full = root.Combine(bookPath.Split('/'));
        Directory.CreateDirectory(SysPath.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return (root, full);
    }

    [Fact]
    public void Derives_bookpath_filename_and_folder_from_paths()
    {
        (TempDir root, string full) = MakeFile("OEBPS/Text/chapter1.xhtml");
        using (root)
        {
            Resource resource = new(root.Path, full);

            resource.BookPath.Should().Be("OEBPS/Text/chapter1.xhtml");
            resource.Filename.Should().Be("chapter1.xhtml");
            resource.Folder.Should().Be("OEBPS/Text");
            resource.Type.Should().Be(ResourceType.Generic);
            resource.Identifier.Should().HaveLength(26);
        }
    }

    [Fact]
    public void CurrentBookRelPath_falls_back_to_bookpath()
    {
        (TempDir root, string full) = MakeFile("OEBPS/img/a.png");
        using (root)
        {
            Resource resource = new(root.Path, full);

            resource.CurrentBookRelPath.Should().Be("OEBPS/img/a.png");
            resource.CurrentBookRelPath = "original/place/a.png";
            resource.CurrentBookRelPath.Should().Be("original/place/a.png");
        }
    }

    [Fact]
    public void RenameTo_moves_the_file_updates_bookpath_and_raises_event()
    {
        (TempDir root, string full) = MakeFile("OEBPS/Text/old.xhtml");
        using (root)
        {
            Resource resource = new(root.Path, full);
            string? oldPathFromEvent = null;
            resource.Renamed += (_, e) => oldPathFromEvent = e.OldFullPath;

            bool ok = resource.RenameTo("new.xhtml");

            ok.Should().BeTrue();
            resource.BookPath.Should().Be("OEBPS/Text/new.xhtml");
            resource.ShortPathName.Should().Be("new.xhtml");
            File.Exists(resource.FullPath).Should().BeTrue();
            File.Exists(full).Should().BeFalse();
            oldPathFromEvent.Should().Be(full);
        }
    }

    [Fact]
    public void RenameTo_fails_when_target_already_exists()
    {
        (TempDir root, string full) = MakeFile("OEBPS/Text/a.xhtml");
        using (root)
        {
            File.WriteAllText(root.Combine("OEBPS", "Text", "b.xhtml"), "y");
            Resource resource = new(root.Path, full);

            resource.RenameTo("b.xhtml").Should().BeFalse();
            resource.BookPath.Should().Be("OEBPS/Text/a.xhtml");
        }
    }

    [Fact]
    public void MoveTo_creates_missing_directories_and_raises_event()
    {
        (TempDir root, string full) = MakeFile("OEBPS/Text/a.xhtml");
        using (root)
        {
            Resource resource = new(root.Path, full);
            bool moved = false;
            resource.Moved += (_, _) => moved = true;

            bool ok = resource.MoveTo("OEBPS/nested/deep/a.xhtml");

            ok.Should().BeTrue();
            moved.Should().BeTrue();
            resource.BookPath.Should().Be("OEBPS/nested/deep/a.xhtml");
            File.Exists(resource.FullPath).Should().BeTrue();
        }
    }

    [Fact]
    public void Delete_removes_the_file_and_raises_event()
    {
        (TempDir root, string full) = MakeFile("OEBPS/x.bin");
        using (root)
        {
            Resource resource = new(root.Path, full);
            bool deleted = false;
            resource.Deleted += (_, _) => deleted = true;

            resource.Delete().Should().BeTrue();

            deleted.Should().BeTrue();
            File.Exists(full).Should().BeFalse();
            resource.Delete().Should().BeFalse();
        }
    }
}
