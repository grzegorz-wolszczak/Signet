using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.Core.BookManipulation;
using SysPath = System.IO.Path;

namespace Signet.Core.Diff;

/// <summary>Kind of file difference between two states of a book.</summary>
public enum BookFileChange
{
    /// <summary>The file exists in both states, but with different content.</summary>
    Modified,

    /// <summary>The file exists only in the "right" (current) state.</summary>
    Added,

    /// <summary>The file exists only in the "left" state (checkpoint).</summary>
    Removed,

    /// <summary>The same content under a different path.</summary>
    Renamed,
}

/// <summary>How to show the file content in a comparison.</summary>
public enum BookFileContentKind
{
    /// <summary>Text — line-by-line diff.</summary>
    Text,

    /// <summary>Raster image — side-by-side previews.</summary>
    Image,

    /// <summary>Other binary file — only a change notice.</summary>
    Binary,
}

/// <summary>A single changed file in a comparison of two book states.</summary>
/// <param name="Change">Kind of change.</param>
/// <param name="LeftPath">Bookpath in the left state (<c>null</c> for <see cref="BookFileChange.Added"/>).</param>
/// <param name="RightPath">Bookpath in the right state (<c>null</c> for <see cref="BookFileChange.Removed"/>).</param>
/// <param name="LeftFullPath">Full on-disk path of the left file, or <c>null</c>.</param>
/// <param name="RightFullPath">Full on-disk path of the right file, or <c>null</c>.</param>
/// <param name="ContentKind">Content kind (by extension).</param>
public sealed record BookFileDiff(
    BookFileChange Change,
    string? LeftPath,
    string? RightPath,
    string? LeftFullPath,
    string? RightFullPath,
    BookFileContentKind ContentKind)
{
    /// <summary>Bookpath to display (the right one, or the left one for removed files).</summary>
    public string DisplayPath => RightPath ?? LeftPath ?? string.Empty;

    /// <summary>The left text when it is not read from a file (a comparison of two texts in memory), otherwise <c>null</c>.</summary>
    public string? LeftText { get; init; }

    /// <summary>The right text when it is not read from a file (a comparison of two texts in memory), otherwise <c>null</c>.</summary>
    public string? RightText { get; init; }

    /// <summary>Content of the left file as text (empty when it does not exist).</summary>
    public string ReadLeftText() => LeftText ?? (LeftFullPath is null ? string.Empty : Utility.ReadUnicodeTextFile(LeftFullPath));

    /// <summary>Content of the right file as text (empty when it does not exist).</summary>
    public string ReadRightText() => RightText ?? (RightFullPath is null ? string.Empty : Utility.ReadUnicodeTextFile(RightFullPath));

    /// <summary>A comparison of two versions of one text file held in memory (e.g. before and after "Mend").</summary>
    public static BookFileDiff FromTexts(string bookPath, string leftText, string rightText) =>
        new(BookFileChange.Modified, bookPath, bookPath, null, null, BookFileContentKind.Text)
        {
            LeftText = leftText,
            RightText = rightText,
        };
}

/// <summary>
/// Comparison of two folders with publication files (states from <see cref="CheckpointHistory"/>).
/// </summary>
/// <remarks>
/// Files with the same path and identical bytes are skipped; a removed and an added file with identical
/// content count as a rename. Technical files of the working folder are skipped (the
/// <see cref="TempFolder"/> lock and <see cref="BookStateFile"/>).
/// </remarks>
public static class BookComparer
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "htm", "xhtml", "xml", "opf", "ncx", "css", "svg", "js", "txt", "smil", "pls", "xpgt",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff",
    };

    /// <summary>
    /// Compares the state <paramref name="leftRoot"/> (e.g. a checkpoint) with the state
    /// <paramref name="rightRoot"/> (e.g. the current one). The result is sorted by path.
    /// </summary>
    public static IReadOnlyList<BookFileDiff> Compare(string leftRoot, string rightRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightRoot);

        Dictionary<string, string> left = ListFiles(leftRoot);
        Dictionary<string, string> right = ListFiles(rightRoot);
        List<BookFileDiff> result = new();

        foreach ((string path, string leftFull) in left)
        {
            if (right.TryGetValue(path, out string? rightFull) && !SameContent(leftFull, rightFull))
            {
                result.Add(new BookFileDiff(BookFileChange.Modified, path, path, leftFull, rightFull, ContentKindOf(path)));
            }
        }

        AddFileSetChanges(left, right, result);
        return result.OrderBy(d => d.DisplayPath, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Like <see cref="Compare"/>, but lists only the files present in one state (<see cref="BookFileChange.Added"/>,
    /// <see cref="BookFileChange.Removed"/>, <see cref="BookFileChange.Renamed"/>) — without reading the files that
    /// exist in both states.
    /// </summary>
    public static IReadOnlyList<BookFileDiff> CompareFileSets(string leftRoot, string rightRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightRoot);

        List<BookFileDiff> result = new();
        AddFileSetChanges(ListFiles(leftRoot), ListFiles(rightRoot), result);
        return result.OrderBy(d => d.DisplayPath, StringComparer.Ordinal).ToList();
    }

    // The files present in only one of the states; a removed and an added file with identical content count as a rename.
    private static void AddFileSetChanges(Dictionary<string, string> left, Dictionary<string, string> right, List<BookFileDiff> result)
    {
        List<string> removed = left.Keys.Where(p => !right.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        List<string> added = right.Keys.Where(p => !left.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        foreach (string removedPath in removed)
        {
            string? renamedTo = added.FirstOrDefault(a => SameContent(left[removedPath], right[a]));
            if (renamedTo is not null)
            {
                added.Remove(renamedTo);
                result.Add(new BookFileDiff(
                    BookFileChange.Renamed, removedPath, renamedTo, left[removedPath], right[renamedTo], ContentKindOf(renamedTo)));
            }
            else
            {
                result.Add(new BookFileDiff(
                    BookFileChange.Removed, removedPath, null, left[removedPath], null, ContentKindOf(removedPath)));
            }
        }

        foreach (string addedPath in added)
        {
            result.Add(new BookFileDiff(BookFileChange.Added, null, addedPath, null, right[addedPath], ContentKindOf(addedPath)));
        }
    }

    /// <summary>Content kind of a file based on the bookpath's extension.</summary>
    public static BookFileContentKind ContentKindOf(string bookPath)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        string extension = SysPath.GetExtension(bookPath).TrimStart('.');
        if (TextExtensions.Contains(extension))
        {
            return BookFileContentKind.Text;
        }

        return ImageExtensions.Contains(extension) ? BookFileContentKind.Image : BookFileContentKind.Binary;
    }

    private static Dictionary<string, string> ListFiles(string root)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return files;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string bookPath = SysPath.GetRelativePath(root, file).Replace(SysPath.DirectorySeparatorChar, '/');
            if (bookPath is TempFolder.LockFileName or BookStateFile.FileName)
            {
                continue;
            }

            files[bookPath] = file;
        }

        return files;
    }

    private static bool SameContent(string leftFile, string rightFile)
    {
        FileInfo leftInfo = new(leftFile);
        FileInfo rightInfo = new(rightFile);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        return File.ReadAllBytes(leftFile).AsSpan().SequenceEqual(File.ReadAllBytes(rightFile));
    }
}
