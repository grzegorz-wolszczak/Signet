using System;
using System.Collections.Generic;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks that the media type each manifest item declares is the one its file extension stands for (a <c>.css</c> file declared
/// <c>text/plain</c>), with an automatic fix (<see cref="ManifestMediaTypeFix"/>). Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// The rest of the checks of the OPF (the manifest, the spine, the metadata, the unique identifier, duplicates, the navigation
/// document) are EPUBCheck's. This one stays because EPUBCheck compares the media type with the content only for the kinds of
/// file it checks, and does not offer the fix. <see cref="ContentTypeValidator"/> compares the media type with the content.
/// </remarks>
public static class ManifestMediaTypeValidator
{
    /// <summary>Runs the check for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        List<ValidationResult> results = new();
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Href.Length == 0 || entry.MediaType.Length == 0)
            {
                continue;
            }

            string expected = MediaTypes.GetMediaTypeFromExtension(GetExtension(entry.Href));
            if (expected.Length == 0)
            {
                // Unknown extension — nothing to compare (e.g. files with no counterpart in the MediaTypes table).
                continue;
            }

            if (!string.Equals(expected, entry.MediaType, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Warning, opf.BookPath, -1, -1,
                    CoreStrings.Format("Validation_OpfMediaTypeMismatch", entry.Href, entry.MediaType, expected),
                    "Validation_OpfMediaTypeMismatch",
                    new ManifestMediaTypeFix(entry.Href, expected)));
            }
        }

        return results;
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
}
