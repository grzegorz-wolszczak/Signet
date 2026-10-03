using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Parsing/structure checks at the whole-book level. Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>, like <see cref="OpfStructureValidator"/>,
/// <see cref="LinkIntegrityValidator"/> and <see cref="FontIntegrityValidator"/>.
/// </summary>
/// <remarks>
/// Four cross-file checks that the per-file <see cref="WellFormedChecker"/> does not do:
/// a duplicate <c>id</c> used in more than one HTML file, an HTML file exceeding a reasonable
/// size limit, text placed directly in <c>&lt;body&gt;</c> (outside any block
/// element) and file bytes that are not valid UTF-8.
/// </remarks>
public static class CrossFileStructureValidator
{
    /// <summary>
    /// The size limit of a single (X)HTML file in UTF-8 bytes above which a warning is reported.
    /// The value (300 000 B ~ 293 KiB) is a deliberately chosen heuristic — the EPUB specification
    /// imposes no hard limit, but very large single XHTML files are a known source of
    /// performance/memory problems in some readers.
    /// </summary>
    public const int MaxHtmlFileSizeBytes = 300_000;

    /// <summary>Runs all the cross-file checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        IReadOnlyList<HtmlResource> htmlResources = book.GetHtmlResources();

        CheckDuplicateIdsAcrossFiles(htmlResources, results);

        foreach (HtmlResource html in htmlResources)
        {
            CheckFileSize(html, results);
            CheckBareBodyText(html, results);
            CheckUtf8Validity(html, results);
        }

        return results;
    }

    private static void CheckDuplicateIdsAcrossFiles(IReadOnlyList<HtmlResource> htmlResources, List<ValidationResult> results)
    {
        // id -> the set of files it occurs in (in order of first encounter).
        Dictionary<string, List<string>> idToBookPaths = new(StringComparer.Ordinal);

        foreach (HtmlResource html in htmlResources)
        {
            IHtmlDocument document = html.GetDocument();
            HashSet<string> idsInThisFile = new(StringComparer.Ordinal);
            foreach (IElement element in document.All)
            {
                string? id = element.GetAttribute("id");
                if (string.IsNullOrEmpty(id) || !idsInThisFile.Add(id))
                {
                    continue;
                }

                if (!idToBookPaths.TryGetValue(id, out List<string>? paths))
                {
                    paths = new List<string>();
                    idToBookPaths[id] = paths;
                }

                paths.Add(html.BookPath);
            }
        }

        foreach ((string id, List<string> paths) in idToBookPaths)
        {
            if (paths.Count < 2)
            {
                continue;
            }

            foreach (string bookPath in paths)
            {
                IEnumerable<string> otherPaths = paths.Where(p => !string.Equals(p, bookPath, StringComparison.Ordinal));
                Add(results, ValidationSeverity.Warning, bookPath,
                    CoreStrings.Format("Validation_DuplicateIdAcrossFiles", id, string.Join(", ", otherPaths)));
            }
        }
    }

    private static void CheckFileSize(HtmlResource html, List<ValidationResult> results)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(html.GetText());
        if (byteCount <= MaxHtmlFileSizeBytes)
        {
            return;
        }

        Add(results, ValidationSeverity.Warning, html.BookPath,
            CoreStrings.Format("Validation_FileTooLarge", byteCount, MaxHtmlFileSizeBytes));
    }

    private static void CheckBareBodyText(HtmlResource html, List<ValidationResult> results)
    {
        IHtmlDocument document = html.GetDocument();
        IHtmlElement? body = document.Body;
        if (body is null)
        {
            return;
        }

        bool hasBareText = body.ChildNodes
            .OfType<IText>()
            .Any(text => !string.IsNullOrWhiteSpace(text.TextContent));

        if (!hasBareText)
        {
            return;
        }

        Add(results, ValidationSeverity.Warning, html.BookPath,
            CoreStrings.Get("Validation_BareBodyText"));
    }

    private static void CheckUtf8Validity(HtmlResource html, List<ValidationResult> results)
    {
        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(html.FullPath);
        }
        catch (IOException)
        {
            // The file is not yet saved to disk (e.g. a newly created in-memory resource) —
            // there is nothing to check at the byte level, so skip it without a false alarm.
            return;
        }

        if (HtmlEncodingResolver.IsValidUtf8(rawBytes))
        {
            return;
        }

        Add(results, ValidationSeverity.Warning, html.BookPath,
            CoreStrings.Get("Validation_InvalidUtf8"));
    }

    private static void Add(List<ValidationResult> results, ValidationSeverity severity, string bookPath, string message) =>
        results.Add(new ValidationResult(severity, bookPath, -1, -1, message));
}
