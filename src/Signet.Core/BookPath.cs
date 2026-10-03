using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core;

/// <summary>
/// Stateless operations on a <em>bookpath</em> - a file path relative to the root folder of the
/// unpacked EPUB (e.g. <c>OEBPS/Text/ch1.xhtml</c>). A bookpath always uses
/// <c>/</c> as the separator and never starts with <c>/</c>.
/// </summary>
/// <remarks>
/// Edge case: a <c>..</c> segment going above the root is silently dropped.
/// </remarks>
public static class BookPath
{
    private const char Separator = '/';
    private const string SeparatorString = "/";

    /// <summary>
    /// Resolves <c>.</c> and <c>..</c> segments in a path (with the <c>/</c> separator).
    /// A <c>..</c> segment without a corresponding parent folder is ignored.
    /// </summary>
    /// <param name="path">Path with <c>/</c> separators.</param>
    /// <returns>Path without <c>.</c> / <c>..</c> segments.</returns>
    public static string ResolveRelativeSegments(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        List<string> result = new();
        foreach (string segment in path.Split(Separator))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (result.Count > 0)
                {
                    result.RemoveAt(result.Count - 1);
                }

                continue;
            }

            result.Add(segment);
        }

        return string.Join(SeparatorString, result);
    }

    /// <summary>
    /// Builds the bookpath of the file referenced by the relative path <paramref name="destRelativePath"/>
    /// resolved against the folder <paramref name="startFolder"/>.
    /// </summary>
    /// <param name="destRelativePath">Relative path (may contain <c>../</c>, <c>./</c>).</param>
    /// <param name="startFolder">Start folder as a bookpath (without a file name); <c>""</c> = root.</param>
    /// <returns>Normalized bookpath of the target file.</returns>
    public static string BuildBookPath(string destRelativePath, string startFolder)
    {
        ArgumentNullException.ThrowIfNull(destRelativePath);
        ArgumentNullException.ThrowIfNull(startFolder);

        string bookPath = startFolder.TrimEnd(Separator);
        bookPath = bookPath.Length != 0
            ? bookPath + SeparatorString + destRelativePath
            : destRelativePath;

        return ResolveRelativeSegments(bookPath);
    }

    /// <summary>
    /// Returns the folder containing the given file.
    /// For a file in the root returns <c>""</c>.
    /// </summary>
    /// <param name="fileBookPath">Bookpath of the file.</param>
    /// <returns>Bookpath of the parent folder, or <c>""</c>.</returns>
    public static string StartingDir(string fileBookPath)
    {
        ArgumentNullException.ThrowIfNull(fileBookPath);

        int lastSeparator = fileBookPath.LastIndexOf(Separator);
        return lastSeparator > -1 ? fileBookPath[..lastSeparator] : string.Empty;
    }

    /// <summary>
    /// Relative path from the folder <paramref name="startDir"/> to <paramref name="destination"/>.
    /// Both arguments are bookpaths; trailing separators
    /// are ignored. An empty <paramref name="startDir"/> returns <paramref name="destination"/>
    /// unchanged.
    /// </summary>
    /// <param name="destination">Bookpath of the target (file or folder).</param>
    /// <param name="startDir">Bookpath of the start folder.</param>
    /// <returns>Relative path with <c>..</c> segments where needed.</returns>
    public static string RelativePath(string destination, string startDir)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(startDir);

        if (startDir.Length == 0)
        {
            return destination;
        }

        string[] destSegments = destination.TrimEnd(Separator).Split(Separator);
        string[] startSegments = startDir.TrimEnd(Separator).Split(Separator);

        int common = 0;
        while (common < startSegments.Length
               && common < destSegments.Length
               && destSegments[common] == startSegments[common])
        {
            common++;
        }

        List<string> result = new();
        for (int up = common; up < startSegments.Length; up++)
        {
            result.Add("..");
        }

        for (int down = common; down < destSegments.Length; down++)
        {
            result.Add(destSegments[down]);
        }

        return string.Join(SeparatorString, result);
    }

    /// <summary>
    /// Relative path leading from the file <paramref name="fromFileBookPath"/> to the file
    /// <paramref name="toFileBookPath"/> - the form that goes e.g. into an <c>href</c>
    /// attribute. Identical paths yield <c>""</c>.
    /// </summary>
    /// <param name="fromFileBookPath">Bookpath of the source file (includes the file name).</param>
    /// <param name="toFileBookPath">Bookpath of the target file.</param>
    /// <returns>Relative path to insert into the source file.</returns>
    public static string Relative(string fromFileBookPath, string toFileBookPath)
    {
        ArgumentNullException.ThrowIfNull(fromFileBookPath);
        ArgumentNullException.ThrowIfNull(toFileBookPath);

        if (fromFileBookPath == toFileBookPath)
        {
            return string.Empty;
        }

        return RelativePath(toFileBookPath, StartingDir(fromFileBookPath));
    }

    /// <summary>
    /// Longest common folder prefix of a set of bookpaths, terminated with <c>/</c>.
    /// Empty set -&gt; <c>""</c>; one element -&gt; that element + <c>/</c>;
    /// no common prefix -&gt; <c>"/"</c>.
    /// </summary>
    /// <param name="filePaths">Collection of bookpaths.</param>
    /// <returns>Common prefix terminated with a separator.</returns>
    public static string LongestCommonPath(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        List<string> paths = filePaths.ToList();
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        if (paths.Count == 1)
        {
            return paths[0] + SeparatorString;
        }

        paths.Sort(StringComparer.Ordinal);
        string[] firstSegments = paths[0].Split(Separator);
        string[] lastSegments = paths[^1].Split(Separator);

        List<string> common = new();
        int i = 0;
        while (i < firstSegments.Length
               && i < lastSegments.Length
               && firstSegments[i] == lastSegments[i])
        {
            common.Add(firstSegments[i]);
            i++;
        }

        if (common.Count == 0)
        {
            return SeparatorString;
        }

        return string.Join(SeparatorString, common) + SeparatorString;
    }
}
