using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Validates the structure of the OPF file (manifest, spine, metadata). Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// The checks are deliberately limited to the internal consistency of the OPF document (parsed by
/// <see cref="OpfDocument"/>) — they do not check whether the files pointed to by <c>href</c>
/// actually exist in the book or whether links inside the (X)HTML work; that is the job of
/// <see cref="LinkIntegrityValidator"/>.
/// </remarks>
public static class OpfStructureValidator
{
    /// <summary>
    /// An extension point for further Check Book checks: each
    /// new category is another private <c>Check*(OpfDocument, string opfBookPath, List&lt;ValidationResult&gt;)</c> method
    /// called from <see cref="Validate"/>, or a separate static class called from here or
    /// directly from <see cref="BookValidator.ValidateCurrentBook"/> — both paths are equivalent,
    /// and the result ends up in the shared <see cref="ValidationResult"/> list either way.
    /// </summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        string opfBookPath = opf.BookPath;
        bool epub3 = document.Version.StartsWith('3');

        List<ValidationResult> results = new();

        CheckRequiredSections(document, opfBookPath, results);
        CheckUniqueIdentifier(document, opfBookPath, results);
        CheckManifestIds(document, opfBookPath, results);
        CheckDuplicateHrefs(document, opfBookPath, results);
        CheckManifestMediaTypes(document, opfBookPath, results);
        CheckSpineIdRefs(document, opfBookPath, results);
        CheckCoverImageCount(document, opfBookPath, epub3, results);
        CheckNavigation(book, opfBookPath, epub3, results);

        return results;
    }

    private static void CheckRequiredSections(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        if (document.Metadata.Count == 0)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfMetadataEmpty", CoreStrings.Get("Validation_OpfMetadataEmpty"));
        }

        if (document.Manifest.Count == 0)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfManifestEmpty", CoreStrings.Get("Validation_OpfManifestEmpty"));
        }

        if (document.Spine.Count == 0)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfSpineEmpty", CoreStrings.Get("Validation_OpfSpineEmpty"));
        }
    }

    private static void CheckUniqueIdentifier(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        string uniqueId = document.UniqueIdentifierId;
        bool hasMatchingDcIdentifier = document.Metadata.Any(m =>
            string.Equals(m.Name, "dc:identifier", StringComparison.OrdinalIgnoreCase) &&
            uniqueId.Length > 0 &&
            string.Equals(m.Attributes.Value("id"), uniqueId, StringComparison.Ordinal));

        if (uniqueId.Length == 0)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfUniqueIdentifierEmpty",
                CoreStrings.Get("Validation_OpfUniqueIdentifierEmpty"), new UniqueIdentifierFix());
        }
        else if (!hasMatchingDcIdentifier)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfNoUniqueIdentifier",
                CoreStrings.Format("Validation_OpfNoUniqueIdentifier", uniqueId), new UniqueIdentifierFix());
        }
        else if (document.Metadata.Any(m =>
            string.Equals(m.Name, "dc:identifier", StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Attributes.Value("id"), uniqueId, StringComparison.Ordinal)
            && m.Content.Trim().Length == 0))
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfUniqueIdentifierValueEmpty",
                CoreStrings.Format("Validation_OpfUniqueIdentifierValueEmpty", uniqueId), new UniqueIdentifierFix());
        }
    }

    private static void CheckManifestIds(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        Dictionary<string, int> idCounts = new(StringComparer.Ordinal);
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Id.Length == 0)
            {
                Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfManifestItemNoId",
                    CoreStrings.Format("Validation_OpfManifestItemNoId", entry.Href));
                continue;
            }

            idCounts[entry.Id] = idCounts.GetValueOrDefault(entry.Id) + 1;
        }

        foreach ((string id, int count) in idCounts)
        {
            if (count > 1)
            {
                Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfDuplicateId",
                    CoreStrings.Format("Validation_OpfDuplicateId", id, count));
            }
        }
    }

    private static void CheckDuplicateHrefs(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        Dictionary<string, int> hrefCounts = new(StringComparer.Ordinal);
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Href.Length == 0)
            {
                continue;
            }

            hrefCounts[entry.Href] = hrefCounts.GetValueOrDefault(entry.Href) + 1;
        }

        foreach ((string href, int count) in hrefCounts)
        {
            if (count > 1)
            {
                Add(results, ValidationSeverity.Warning, opfBookPath, "Validation_OpfDuplicateHref",
                    CoreStrings.Format("Validation_OpfDuplicateHref", href, count));
            }
        }
    }

    private static void CheckManifestMediaTypes(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Href.Length == 0 || entry.MediaType.Length == 0)
            {
                continue;
            }

            string extension = GetExtension(entry.Href);
            string expected = MediaTypes.GetMediaTypeFromExtension(extension);
            if (expected.Length == 0)
            {
                // Unknown extension — nothing to compare (e.g. files with no counterpart in the MediaTypes table).
                continue;
            }

            if (!string.Equals(expected, entry.MediaType, StringComparison.OrdinalIgnoreCase))
            {
                Add(results, ValidationSeverity.Warning, opfBookPath, "Validation_OpfMediaTypeMismatch",
                    CoreStrings.Format("Validation_OpfMediaTypeMismatch", entry.Href, entry.MediaType, expected),
                    new ManifestMediaTypeFix(entry.Href, expected));
            }
        }
    }

    private static void CheckSpineIdRefs(OpfDocument document, string opfBookPath, List<ValidationResult> results)
    {
        HashSet<string> manifestIds = document.Manifest
            .Where(e => e.Id.Length > 0)
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (SpineEntry itemref in document.Spine)
        {
            if (itemref.IdRef.Length == 0)
            {
                Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfItemrefNoIdref", CoreStrings.Get("Validation_OpfItemrefNoIdref"));
                continue;
            }

            if (!manifestIds.Contains(itemref.IdRef))
            {
                Add(results, ValidationSeverity.Error, opfBookPath, "Validation_OpfItemrefUnknownIdref",
                    CoreStrings.Format("Validation_OpfItemrefUnknownIdref", itemref.IdRef));
            }
        }
    }

    private static void CheckCoverImageCount(OpfDocument document, string opfBookPath, bool epub3, List<ValidationResult> results)
    {
        int coverCount;
        if (epub3)
        {
            coverCount = document.Manifest.Count(e =>
                e.Attributes.Value("properties").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("cover-image", StringComparer.Ordinal));
        }
        else
        {
            coverCount = document.Metadata.Count(m =>
                string.Equals(m.Name, "meta", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Attributes.Value("name"), "cover", StringComparison.Ordinal));
        }

        if (coverCount > 1)
        {
            Add(results, ValidationSeverity.Warning, opfBookPath, "Validation_OpfMultipleCovers",
                CoreStrings.Format("Validation_OpfMultipleCovers", coverCount));
        }
    }

    // An EPUB 3 book needs a navigation document, an EPUB 2 book an NCX.
    private static void CheckNavigation(Book book, string opfBookPath, bool epub3, List<ValidationResult> results)
    {
        if (epub3 && book.GetNavResource() is null)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_NoNav", CoreStrings.Get("Validation_NoNav"));
        }
        else if (!epub3 && book.GetNcx() is null)
        {
            Add(results, ValidationSeverity.Error, opfBookPath, "Validation_NoNcx", CoreStrings.Get("Validation_NoNcx"));
        }
    }

    private static readonly System.Buffers.SearchValues<char> QueryOrFragmentChars =
        System.Buffers.SearchValues.Create("?#");

    private static string GetExtension(string href)
    {
        int query = href.AsSpan().IndexOfAny(QueryOrFragmentChars);
        string clean = query >= 0 ? href[..query] : href;
        int dot = clean.LastIndexOf('.');
        return dot >= 0 ? clean[(dot + 1)..] : string.Empty;
    }

    private static void Add(
        List<ValidationResult> results, ValidationSeverity severity, string bookPath, string code, string message, ValidationFix? fix = null) =>
        results.Add(new ValidationResult(severity, bookPath, -1, -1, message, code, fix));
}
