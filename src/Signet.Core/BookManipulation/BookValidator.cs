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
public sealed record ValidationResult(
    ValidationSeverity Severity,
    string BookPath,
    int Line,
    int CharOffset,
    string Message);

/// <summary>
/// Whole-book validation — "Well-Formed Check EPUB" (F7) plus the additional checks listed in
/// <see cref="ValidateCurrentBook"/>, built on <see cref="WellFormedChecker.CheckXhtmlStructure"/>.
/// </summary>
public static class BookValidator
{
    /// <summary>
    /// Checks the structural correctness (well-formedness) of every XHTML resource of the book (including
    /// the nav). Results are ordered like <see cref="Book.GetHtmlResources"/> (spine order).
    /// </summary>
    public static IReadOnlyList<ValidationResult> ValidateCurrentBook(Book book)
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
                        ValidationSeverity.Warning, html.BookPath, LineOfOffset(text, warning.Offset), -1, warning.Message));
                }

                continue;
            }

            // The char offset is always -1 for this kind of validation; the column is appended to the
            // message instead.
            string message = check.Column > 0
                ? CoreStrings.Format("Validation_NearColumn", check.Message, check.Column)
                : check.Message;

            results.Add(new ValidationResult(ValidationSeverity.Error, html.BookPath, check.Line, -1, message));
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
