using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Every place in a book that refers to an element <c>id</c> — so a Cleanup step can warn only when an <c>id</c> it
/// would remove is really used, instead of for every <c>id</c>:
/// <list type="bullet">
/// <item>references with a fragment (<c>href="ch2.xhtml#x"</c>, <c>href="#x"</c>, <c>src</c>, <c>xlink:href</c>,
/// <c>usemap</c>, <c>url(#x)</c> in a <c>style</c> attribute…) in the XHTML files, resolved to the target file;</item>
/// <item>ID references without <c>#</c> in the XHTML files (<c>for</c>, <c>headers</c>, <c>list</c>, <c>form</c>,
/// <c>itemref</c> and every <c>aria-*</c> attribute — each token counts), in the same file;</item>
/// <item><c>href</c>/<c>src</c> attributes with a fragment in the other text files (OPF guide and manifest, NCX, SVG,
/// SMIL…), resolved from their folder;</item>
/// <item>scripts (JavaScript files and <c>&lt;script&gt;</c> elements): an <c>id</c> whose text appears anywhere in a
/// script counts as used, since a script can reach it in ways that cannot be checked.</item>
/// </list>
/// </summary>
internal sealed partial class IdReferenceIndex
{
    private static readonly HashSet<string> IdRefAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "for", "headers", "list", "form", "itemref",
    };

    private readonly HashSet<string> _targets = new(StringComparer.Ordinal);
    private readonly List<string> _scripts = new();

    /// <summary>Indexes the references of <paramref name="book"/>.</summary>
    public IdReferenceIndex(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        HashSet<string> javascript = book.GetJavascriptResources().Select(r => r.BookPath).ToHashSet(StringComparer.Ordinal);

        foreach (Resource resource in book.GetAllResources())
        {
            if (javascript.Contains(resource.BookPath))
            {
                _scripts.Add(resource is TextResource script ? script.GetText() : ReadText(resource.FullPath));
            }
            else if (resource is HtmlResource html)
            {
                foreach (IElement element in html.GetDocument().All)
                {
                    IndexElement(element, html.BookPath, html.Folder);
                }
            }
            else if (resource is TextResource text and not CssResource)
            {
                foreach (Match match in ReferenceAttributeRegex().Matches(text.GetText()))
                {
                    AddReference(match.Groups["value"].Value, text.BookPath, text.Folder);
                }
            }
        }
    }

    /// <summary>
    /// Whether anything in the book refers to the element with <paramref name="id"/> in the file
    /// <paramref name="bookPath"/> (or a script mentions the <c>id</c>).
    /// </summary>
    public bool IsReferenced(string bookPath, string id)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        ArgumentNullException.ThrowIfNull(id);
        return _targets.Contains(Key(bookPath, id)) || _scripts.Any(s => s.Contains(id, StringComparison.Ordinal));
    }

    private void IndexElement(IElement element, string bookPath, string folder)
    {
        if (string.Equals(element.LocalName, "script", StringComparison.OrdinalIgnoreCase))
        {
            _scripts.Add(element.TextContent);
        }

        foreach (IAttr attribute in element.Attributes)
        {
            string name = attribute.LocalName;
            string value = attribute.Value;
            if (IdRefAttributes.Contains(name) || name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    _targets.Add(Key(bookPath, token));
                }
            }

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in UrlRegex().Matches(value))
                {
                    AddReference(match.Groups["value"].Value, bookPath, folder);
                }
            }
            else if (value.Contains('#', StringComparison.Ordinal))
            {
                AddReference(value, bookPath, folder);
            }
        }
    }

    // A reference with a fragment, resolved to its file (an empty path = the referring file itself).
    private void AddReference(string reference, string ownerBookPath, string ownerFolder)
    {
        string trimmed = reference.Trim();
        if (!trimmed.Contains('#', StringComparison.Ordinal) || LinkReference.IsExternal(trimmed))
        {
            return;
        }

        (string path, string fragment) = LinkReference.Split(trimmed);
        if (fragment.Length > 0)
        {
            _targets.Add(Key(path.Length == 0 ? ownerBookPath : BookPath.BuildBookPath(path, ownerFolder), fragment));
        }
    }

    private static string Key(string bookPath, string id) => bookPath + "#" + id;

    private static string ReadText(string fullPath)
    {
        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"(?:[\w-]+:)?(?:href|src)\s*=\s*(?<q>[""'])(?<value>.*?)\k<q>", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceAttributeRegex();

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<value>[^""')]*)\k<q>\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
