using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Misc;

namespace Signet.Core.Parsers;

/// <summary>
/// <c>@import</c> handling: which stylesheets a CSS text imports, and which stylesheets a document actually sees
/// (its linked stylesheets and the <c>@import</c>s of those and of its <c>&lt;style&gt;</c> blocks, transitively).
/// </summary>
public static class CssImports
{
    private static readonly Regex CommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ImportRegex = new(
        @"@import\s+(?:url\(\s*)?[""']?(?<href>[^""')\s;]+)[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The book paths of the stylesheets <paramref name="cssText"/> imports (source order, comments ignored),
    /// resolved against <paramref name="folder"/> — the folder of the stylesheet, or of the XHTML file for a
    /// <c>&lt;style&gt;</c> block. External references are skipped.
    /// </summary>
    public static IReadOnlyList<string> Parse(string cssText, string folder)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        ArgumentNullException.ThrowIfNull(folder);

        List<string> imports = new();
        foreach (Match match in ImportRegex.Matches(CommentRegex.Replace(cssText, string.Empty)))
        {
            if (LinkReference.ResolveBookPath(match.Groups["href"].Value, folder) is { } resolved)
            {
                imports.Add(resolved);
            }
        }

        return imports;
    }

    /// <summary>The book paths of the stylesheets imported by the <c>&lt;style&gt;</c> blocks of an XHTML file.</summary>
    public static IReadOnlyList<string> FromStyleBlocks(string htmlText, string htmlBookPath)
    {
        ArgumentNullException.ThrowIfNull(htmlText);
        ArgumentNullException.ThrowIfNull(htmlBookPath);

        string folder = BookPath.StartingDir(htmlBookPath);
        return new HtmlStyleInfo(htmlText).StyleBlockTexts.SelectMany(text => Parse(text, folder)).ToList();
    }

    /// <summary>
    /// The stylesheets a document sees, in cascade order: every root (a linked stylesheet or a stylesheet imported
    /// by a <c>&lt;style&gt;</c> block) preceded by what it imports, recursively; each stylesheet once (cycles are
    /// cut). <paramref name="importsOf"/> returns the imports of a stylesheet, or <c>null</c> when it is not in the book.
    /// </summary>
    public static IReadOnlyList<string> VisibleStylesheets(IEnumerable<string> roots, Func<string, IReadOnlyList<string>?> importsOf)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(importsOf);

        List<string> result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        void Add(string bookPath)
        {
            if (!seen.Add(bookPath) || importsOf(bookPath) is not { } imports)
            {
                return;
            }

            foreach (string imported in imports)
            {
                Add(imported);
            }

            result.Add(bookPath);
        }

        foreach (string root in roots)
        {
            Add(root);
        }

        return result;
    }
}
