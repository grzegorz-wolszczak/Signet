using System.Collections.Generic;

namespace Signet.Core.BookManipulation;

/// <summary>A file a whole-book operation left alone, and why.</summary>
/// <param name="BookPath">The bookpath of the skipped file.</param>
/// <param name="Reason">Why the file was skipped (e.g. the first well-formedness error).</param>
/// <param name="Line">Line of the problem (1-based), or <c>-1</c> when unknown.</param>
/// <param name="Column">Column of the problem (1-based), or <c>-1</c> when unknown.</param>
public sealed record SkippedFile(string BookPath, string Reason, int Line = -1, int Column = -1);

/// <summary>
/// The result of an operation that is applied file by file ("Mend &amp; Prettify All HTML Files", "Mend All HTML Files",
/// "Add / Remove Soft Hyphens", "Link Stylesheets / Javascripts"): a file that cannot be processed (it is not
/// well-formed, or processing it failed) is skipped and the operation goes on with the rest.
/// </summary>
/// <param name="Processed">The number of files the operation went through without a problem.</param>
/// <param name="Modified">Whether at least one file was changed.</param>
/// <param name="Skipped">The files left alone, with the reason.</param>
public sealed record FileBatchResult(int Processed, bool Modified, IReadOnlyList<SkippedFile> Skipped)
{
    /// <summary>Whether at least one file was skipped.</summary>
    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>Whether there were files to process and none of them could be (so the book is unchanged).</summary>
    public bool NothingProcessed => Processed == 0 && Skipped.Count > 0;
}
