using System;
using System.Collections.Generic;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>The severity of a validation result.</summary>
public enum ValidationSeverity
{
    /// <summary>Information.</summary>
    Info,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,
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

/// <summary>
/// Whole-book validation — "Well-Formed Check EPUB" (F7) plus the additional checks listed in
/// <see cref="ValidateCurrentBook"/>, built on <see cref="WellFormedChecker.CheckXhtmlStructure"/>.
/// </summary>
public static class BookValidator
{
    /// <summary>The rule code of a file that is not well-formed.</summary>
    public const string NotWellFormedCode = "WellFormed_NotWellFormed";

    /// <summary>
    /// Checks the structural correctness (well-formedness) of every XHTML resource of the book (including
    /// the nav). Results are ordered like <see cref="Book.GetHtmlResources"/> (spine order).
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="skippedCodes">The rule codes (<see cref="ValidationResult.Code"/>) whose results are left out.</param>
    public static IReadOnlyList<ValidationResult> ValidateCurrentBook(Book book, IReadOnlyCollection<string>? skippedCodes = null)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            WellFormedResult check = WellFormedChecker.CheckXhtmlStructure(text, html.EpubVersion);
            if (check.IsWellFormed)
            {
                // A missing DOCTYPE does not make the file invalid — reported as a warning only.
                if (check.Warning is { } warning)
                {
                    results.Add(new ValidationResult(
                        ValidationSeverity.Warning, html.BookPath, LineOfOffset(text, warning.Offset), -1, warning.Message,
                        "WellFormed_DoctypeMissing"));
                }

                continue;
            }

            // The char offset is always -1 for this kind of validation; the column is appended to the
            // message instead.
            string message = check.Column > 0
                ? CoreStrings.Format("Validation_NearColumn", check.Message, check.Column)
                : check.Message;

            results.Add(new ValidationResult(ValidationSeverity.Error, html.BookPath, check.Line, -1, message, NotWellFormedCode));
        }

        // Additional checks: OPF structure, link/reference integrity, font checks,
        // cross-file structure, file name portability, mimetype vs. content consistency —
        // further categories are added here the same way, each as a separate
        // call returning an IReadOnlyList<ValidationResult>.
        results.AddRange(OpfStructureValidator.Validate(book));
        results.AddRange(LinkIntegrityValidator.Validate(book));
        results.AddRange(FontIntegrityValidator.Validate(book));
        results.AddRange(CrossFileStructureValidator.Validate(book));
        results.AddRange(FileNamePortabilityValidator.Validate(book));
        results.AddRange(ContentTypeValidator.Validate(book));
        results.AddRange(IdValidator.Validate(book));
        results.AddRange(ImageIntegrityValidator.Validate(book));
        results.AddRange(CssPropertyValidator.Validate(book));

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
