using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Diff;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Diff;

/// <summary>Tests of <see cref="BookComparer"/> — the list of changed files between two states.</summary>
public sealed class BookComparerTests
{
    private static (string Left, string Right) TwoCopies(TempDir temp)
    {
        string left = TestFs.CopyDirectory(CorpusPaths.Epub3Media, temp.Combine("left"));
        string right = TestFs.CopyDirectory(CorpusPaths.Epub3Media, temp.Combine("right"));
        return (left, right);
    }

    private static string FirstFile(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).OrderBy(f => f).First();

    private static string BookPath(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void Identical_states_have_no_differences()
    {
        using TempDir temp = new();
        (string left, string right) = TwoCopies(temp);

        BookComparer.Compare(left, right).Should().BeEmpty();
    }

    [Fact]
    public void Detects_modified_added_removed_and_renamed_files()
    {
        using TempDir temp = new();
        (string left, string right) = TwoCopies(temp);
        string modified = FirstFile(right, "*.css");
        File.AppendAllText(modified, "\np { color: red }\n");
        File.WriteAllText(Path.Combine(right, "EPUB", "added.txt"), "new");
        string removed = FirstFile(right, "*.xhtml");
        string removedBookPath = BookPath(right, removed);
        File.Delete(removed);
        string image = Directory.EnumerateFiles(right, "*.*", SearchOption.AllDirectories)
            .First(f => BookComparer.ContentKindOf(f) == BookFileContentKind.Image);
        string imageBookPath = BookPath(right, image);
        File.Move(image, Path.Combine(Path.GetDirectoryName(image)!, "renamed" + Path.GetExtension(image)));

        IReadOnlyList<BookFileDiff> diffs = BookComparer.Compare(left, right);

        diffs.Should().ContainSingle(d => d.Change == BookFileChange.Modified && d.RightPath == BookPath(right, modified))
            .Which.ContentKind.Should().Be(BookFileContentKind.Text);
        diffs.Should().ContainSingle(d => d.Change == BookFileChange.Added && d.RightPath == "EPUB/added.txt");
        diffs.Should().ContainSingle(d => d.Change == BookFileChange.Removed && d.LeftPath == removedBookPath);
        diffs.Should().ContainSingle(d => d.Change == BookFileChange.Renamed && d.LeftPath == imageBookPath)
            .Which.ContentKind.Should().Be(BookFileContentKind.Image);
        diffs.Should().HaveCount(4);
    }

    [Fact]
    public void Ignores_the_working_folder_technical_files()
    {
        using TempDir temp = new();
        (string left, string right) = TwoCopies(temp);
        File.WriteAllText(Path.Combine(right, BookStateFile.FileName), "{}");
        File.WriteAllText(Path.Combine(right, TempFolder.LockFileName), string.Empty);

        BookComparer.Compare(left, right).Should().BeEmpty();
    }

    [Fact]
    public void Text_of_both_sides_is_readable()
    {
        using TempDir temp = new();
        (string left, string right) = TwoCopies(temp);
        string css = FirstFile(right, "*.css");
        File.AppendAllText(css, "\n/* x */\n");

        BookFileDiff diff = BookComparer.Compare(left, right).Single();

        diff.ReadRightText().Should().Contain("/* x */");
        diff.ReadLeftText().Should().NotContain("/* x */");
    }
}
