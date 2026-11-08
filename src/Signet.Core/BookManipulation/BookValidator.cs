using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>The severity of a validation result, from the least to the most serious (EPUBCheck's levels).</summary>
public enum ValidationSeverity
{
    /// <summary>Information about the features the book uses (EPUBCheck's usage messages); reported only when asked for.</summary>
    Usage,

    /// <summary>Information.</summary>
    Info,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,

    /// <summary>A fatal error: the check cannot go on with the file or the book.</summary>
    Fatal,
}

/// <summary>
/// A single entry in the "Validation Results" panel.
/// </summary>
/// <param name="Severity">The severity of the result.</param>
/// <param name="BookPath">The bookpath of the resource the result concerns.</param>
/// <param name="Line">The line number (1-based) or <c>-1</c> when unknown.</param>
/// <param name="CharOffset">The character offset within the line or <c>-1</c> when unknown/unused.</param>
/// <param name="Message">The message.</param>
/// <param name="Code">
/// The rule that produced the result: the resource key of its message (e.g. <c>Validation_DeadLink</c>). A whole
/// rule can be skipped by its code (<see cref="BookValidator.ValidateCurrentBook"/>).
/// </param>
/// <param name="Fix">The automatic fix of the problem, or <c>null</c> when it has none.</param>
public sealed record ValidationResult(
    ValidationSeverity Severity,
    string BookPath,
    int Line,
    int CharOffset,
    string Message,
    string Code = "",
    ValidationFix? Fix = null);

/// <summary>
/// An automatic fix of a <see cref="ValidationResult"/> ("Fix" in the Validation Results panel). A fix looks the
/// problem up again when it is applied, so several fixes can run one after another on the changed book.
/// </summary>
public abstract class ValidationFix
{
    /// <summary>Applies the fix; <c>false</c> when there was nothing (left) to change.</summary>
    public abstract bool Apply(Book book);
}

/// <summary>The part of the whole-book validation that is running (<see cref="ValidationProgress.Stage"/>).</summary>
public enum ValidationStage
{
    /// <summary>EPUBCheck (<see cref="ValidationProgress.Done"/> of <see cref="ValidationProgress.Total"/> files seen so far).</summary>
    EpubCheck,

    /// <summary>The well-formedness of each (X)HTML file (<see cref="ValidationProgress.Done"/> of <see cref="ValidationProgress.Total"/>).</summary>
    WellFormed,

    /// <summary>The media types of the manifest against the file extensions.</summary>
    ManifestMediaTypes,

    /// <summary>The fonts.</summary>
    Fonts,

    /// <summary>The structure across files.</summary>
    CrossFile,

    /// <summary>The portability of the file names.</summary>
    FileNames,

    /// <summary>The media types against the content.</summary>
    ContentTypes,

    /// <summary>The ids.</summary>
    Ids,

    /// <summary>The images.</summary>
    Images,

    /// <summary>The CSS properties.</summary>
    Css,

    /// <summary>The footnotes.</summary>
    Footnotes,

    /// <summary>The accessibility.</summary>
    Accessibility,
}

/// <summary>Where <see cref="BookValidator.ValidateCurrentBook"/> is.</summary>
/// <param name="Stage">What it is checking.</param>
/// <param name="Done">For <see cref="ValidationStage.WellFormed"/>: the files checked before the current one.</param>
/// <param name="Total">For <see cref="ValidationStage.WellFormed"/>: the files to check.</param>
public sealed record ValidationProgress(ValidationStage Stage, int Done = 0, int Total = 0);

/// <summary>
/// What a caller may ask of the whole-book validation: to be told where it is, and to be able to stop it (an
/// <see cref="OperationCanceledException"/> between two steps; the validation only reads the book, so nothing is left to undo).
/// </summary>
/// <param name="Progress">Told before each file and each further check; <c>null</c> for nobody.</param>
/// <param name="Cancellation">Checked before each file and each further check.</param>
public readonly record struct ValidationRun(IProgress<ValidationProgress>? Progress = null, CancellationToken Cancellation = default);

/// <summary>
/// Whole-book validation — "Well-Formed Check EPUB" (F7): EPUBCheck plus the checks of Signet's own listed in
/// <see cref="ValidateCurrentBook"/>.
/// </summary>
public static class BookValidator
{
    /// <summary>
    /// Checks the book: what EPUBCheck reports (<see cref="EpubCheckValidator"/>), then Signet's own checks that EPUBCheck does
    /// not make (a missing DOCTYPE and the validators listed in the code).
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="skippedCodes">The rule codes (<see cref="ValidationResult.Code"/>) whose results are left out.</param>
    /// <param name="run">Reports where the validation is and can stop it.</param>
    /// <param name="culture">The language of the messages of EPUBCheck; the current UI language by default.</param>
    /// <param name="includeUsage">Whether EPUBCheck's usage messages are reported.</param>
    public static IReadOnlyList<ValidationResult> ValidateCurrentBook(
        Book book,
        IReadOnlyCollection<string>? skippedCodes = null,
        ValidationRun run = default,
        CultureInfo? culture = null,
        bool includeUsage = false)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        run.Progress?.Report(new ValidationProgress(ValidationStage.EpubCheck));
        results.AddRange(EpubCheckValidator.Validate(book, skippedCodes, culture, includeUsage, run));
        // EPUBCheck reports files that are not well-formed (RSC-016); a missing DOCTYPE is not an error for it, but Signet warns.
        List<HtmlResource> files = book.GetHtmlResources().ToList();
        for (int i = 0; i < files.Count; i++)
        {
            run.Cancellation.ThrowIfCancellationRequested();
            run.Progress?.Report(new ValidationProgress(ValidationStage.WellFormed, i, files.Count));
            HtmlResource html = files[i];
            string text = html.GetText();
            WellFormedResult check = WellFormedChecker.CheckXhtmlStructure(text, html.EpubVersion);
            if (check.IsWellFormed && check.Warning is { } warning)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Warning, html.BookPath, LineOfOffset(text, warning.Offset), -1, warning.Message,
                    "WellFormed_DoctypeMissing"));
            }
        }

        // Signet's own checks, which EPUBCheck does not make: the media types of the manifest against the extensions, fonts,
        // cross-file structure, file name portability, media types against the content, ids, images, CSS declarations and
        // footnotes. Further categories are added here the same way, each as a separate call returning an
        // IReadOnlyList<ValidationResult>.
        void Check(ValidationStage stage, Func<IReadOnlyList<ValidationResult>> validate)
        {
            run.Cancellation.ThrowIfCancellationRequested();
            run.Progress?.Report(new ValidationProgress(stage));
            results.AddRange(validate());
        }

        Check(ValidationStage.ManifestMediaTypes, () => ManifestMediaTypeValidator.Validate(book));
        Check(ValidationStage.Fonts, () => FontIntegrityValidator.Validate(book));
        Check(ValidationStage.CrossFile, () => CrossFileStructureValidator.Validate(book));
        Check(ValidationStage.FileNames, () => FileNamePortabilityValidator.Validate(book));
        Check(ValidationStage.ContentTypes, () => ContentTypeValidator.Validate(book));
        Check(ValidationStage.Ids, () => IdValidator.Validate(book));
        Check(ValidationStage.Images, () => ImageIntegrityValidator.Validate(book));
        Check(ValidationStage.Css, () => CssPropertyValidator.Validate(book));
        Check(ValidationStage.Footnotes, () => FootnoteValidator.Validate(book));
        Check(ValidationStage.Accessibility, () => AccessibilityValidator.Validate(book));

        if (skippedCodes is { Count: > 0 })
        {
            HashSet<string> skipped = new(skippedCodes, StringComparer.Ordinal);
            results.RemoveAll(r => skipped.Contains(r.Code));
        }

        return results;
    }

    private static int LineOfOffset(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
