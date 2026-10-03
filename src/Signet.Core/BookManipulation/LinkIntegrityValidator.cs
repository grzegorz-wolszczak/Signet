using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Link/reference checks. Called from <see cref="BookValidator.ValidateCurrentBook"/>,
/// like <see cref="OpfStructureValidator"/>.
/// </summary>
/// <remarks>
/// Checks four things for every href/src reference in (X)HTML and every <c>url()</c> in CSS:
/// whether the target file exists at all (broken link), whether it exists with exactly that letter
/// case (case-mismatch — a cross-platform problem: on case-sensitive file systems links that
/// "work" on Windows/macOS are dead), whether the fragment (<c>#id</c>) points at an existing
/// <c>id</c>/<c>name</c> in the target document, and whether a file that exists in the book is
/// declared in the OPF manifest. External links (a scheme with a colon: <c>http:</c>,
/// <c>mailto:</c>, <c>data:</c>, …) are skipped — checking whether they are reachable is out of scope.
/// </remarks>
public static class LinkIntegrityValidator
{
    private static readonly string[] LinkAttributeTags =
    {
        "a", "img", "link", "script", "source", "audio", "video", "iframe", "object", "embed",
    };

    private static readonly Regex CssUrlPattern =
        new(@"url\(\s*(['""]?)(?<ref>[^'"")]+)\1\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Runs all the link/reference checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        HashSet<string> allBookPaths = book.GetAllResources()
            .Select(r => r.BookPath)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, string> lowerToActual = new(StringComparer.Ordinal);
        foreach (string bookPath in allBookPaths)
        {
            string lower = bookPath.ToLowerInvariant();
            if (!lowerToActual.ContainsKey(lower))
            {
                lowerToActual[lower] = bookPath;
            }
        }

        HashSet<string> manifestBookPaths = BuildManifestBookPaths(book);

        List<ValidationResult> results = new();

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            CheckHtmlLinks(book, html, allBookPaths, lowerToActual, manifestBookPaths, results);
        }

        foreach (CssResource css in book.GetCssResources())
        {
            CheckCssLinks(css, allBookPaths, lowerToActual, manifestBookPaths, results);
        }

        return results;
    }

    private static HashSet<string> BuildManifestBookPaths(Book book)
    {
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();

        HashSet<string> manifestBookPaths = new(StringComparer.Ordinal);
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Href.Length == 0)
            {
                continue;
            }

            string bookPath = Core.BookPath.BuildBookPath(Core.Utility.UrlDecodePath(entry.Href), opf.Folder);
            manifestBookPaths.Add(bookPath);
        }

        return manifestBookPaths;
    }

    private static void CheckHtmlLinks(
        Book book,
        HtmlResource html,
        HashSet<string> allBookPaths,
        Dictionary<string, string> lowerToActual,
        HashSet<string> manifestBookPaths,
        List<ValidationResult> results)
    {
        IDocument document = html.GetDocument();
        foreach (IElement element in document.All)
        {
            if (!LinkAttributeTags.Contains(element.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string? rawHref = element.GetAttribute("href");
            if (rawHref is not null)
            {
                CheckReference(book, html, rawHref, document, allBookPaths, lowerToActual, manifestBookPaths, results);
            }

            string? rawSrc = element.GetAttribute("src");
            if (rawSrc is not null)
            {
                CheckReference(book, html, rawSrc, document, allBookPaths, lowerToActual, manifestBookPaths, results);
            }
        }
    }

    private static void CheckReference(
        Book book,
        HtmlResource owner,
        string rawReference,
        IDocument ownerDocument,
        HashSet<string> allBookPaths,
        Dictionary<string, string> lowerToActual,
        HashSet<string> manifestBookPaths,
        List<ValidationResult> results)
    {
        if (rawReference.Length == 0 || IsExternalReference(rawReference))
        {
            return;
        }

        SplitReference(rawReference, out string pathPart, out string fragment);

        if (pathPart.Length == 0)
        {
            // A fragment-only reference (e.g. href="#chapter-2") — the target is the same document.
            if (fragment.Length > 0 && !HasAnchor(ownerDocument, fragment))
            {
                Add(results, ValidationSeverity.Error, owner.BookPath,
                    CoreStrings.Format("Validation_BadFragmentSameFile", fragment));
            }

            return;
        }

        string targetBookPath = Core.BookPath.BuildBookPath(pathPart, owner.Folder);

        if (allBookPaths.Contains(targetBookPath))
        {
            if (fragment.Length > 0 && !TargetHasAnchor(book, targetBookPath, fragment))
            {
                Add(results, ValidationSeverity.Error, owner.BookPath,
                    CoreStrings.Format("Validation_BadFragment", rawReference, fragment, targetBookPath));
            }

            if (!manifestBookPaths.Contains(targetBookPath))
            {
                Add(results, ValidationSeverity.Warning, owner.BookPath,
                    CoreStrings.Format("Validation_LinkTargetNotInManifest", rawReference, targetBookPath));
            }

            return;
        }

        string lower = targetBookPath.ToLowerInvariant();
        if (lowerToActual.TryGetValue(lower, out string? actual))
        {
            Add(results, ValidationSeverity.Warning, owner.BookPath,
                CoreStrings.Format("Validation_LinkCaseMismatch", rawReference, actual));
            return;
        }

        Add(results, ValidationSeverity.Error, owner.BookPath,
            CoreStrings.Format("Validation_DeadLink", rawReference));
    }

    private static void CheckCssLinks(
        CssResource css,
        HashSet<string> allBookPaths,
        Dictionary<string, string> lowerToActual,
        HashSet<string> manifestBookPaths,
        List<ValidationResult> results)
    {
        string text = css.GetText();
        foreach (Match match in CssUrlPattern.Matches(text))
        {
            string rawReference = match.Groups["ref"].Value.Trim();
            if (rawReference.Length == 0 || IsExternalReference(rawReference))
            {
                continue;
            }

            SplitReference(rawReference, out string pathPart, out _);
            if (pathPart.Length == 0)
            {
                continue;
            }

            string targetBookPath = Core.BookPath.BuildBookPath(pathPart, css.Folder);

            if (allBookPaths.Contains(targetBookPath))
            {
                if (!manifestBookPaths.Contains(targetBookPath))
                {
                    Add(results, ValidationSeverity.Warning, css.BookPath,
                        CoreStrings.Format("Validation_CssUrlNotInManifest", rawReference, targetBookPath));
                }

                continue;
            }

            string lower = targetBookPath.ToLowerInvariant();
            if (lowerToActual.TryGetValue(lower, out string? actual))
            {
                Add(results, ValidationSeverity.Warning, css.BookPath,
                    CoreStrings.Format("Validation_CssUrlCaseMismatch", rawReference, actual));
                continue;
            }

            Add(results, ValidationSeverity.Error, css.BookPath,
                CoreStrings.Format("Validation_CssDeadUrl", rawReference));
        }
    }

    private static bool TargetHasAnchor(Book book, string targetBookPath, string fragment)
    {
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(targetBookPath) is not HtmlResource target)
        {
            // The target is not an (X)HTML document (e.g. an image with a fragment — rare/invalid) —
            // there is nothing to check for id/name, so no false error is reported.
            return true;
        }

        return HasAnchor(target.GetDocument(), fragment);
    }

    private static bool HasAnchor(IDocument document, string fragment) =>
        document.All.Any(e =>
            string.Equals(e.GetAttribute("id"), fragment, StringComparison.Ordinal) ||
            string.Equals(e.GetAttribute("name"), fragment, StringComparison.Ordinal));

    private static bool IsExternalReference(string reference)
    {
        if (reference.StartsWith('#'))
        {
            return false;
        }

        int colon = reference.IndexOf(':', StringComparison.Ordinal);
        int firstSlash = reference.IndexOf('/', StringComparison.Ordinal);
        return colon >= 0 && (firstSlash < 0 || colon < firstSlash);
    }

    private static void SplitReference(string reference, out string pathPart, out string fragment)
    {
        int hash = reference.IndexOf('#', StringComparison.Ordinal);
        string withoutFragment = hash >= 0 ? reference[..hash] : reference;
        fragment = hash >= 0 ? reference[(hash + 1)..] : string.Empty;

        int query = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        pathPart = query >= 0 ? withoutFragment[..query] : withoutFragment;
    }

    private static void Add(List<ValidationResult> results, ValidationSeverity severity, string bookPath, string message) =>
        results.Add(new ValidationResult(severity, bookPath, -1, -1, message));
}
