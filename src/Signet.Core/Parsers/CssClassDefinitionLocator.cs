using System;
using System.Collections.Generic;
using Signet.Core.Resources;

namespace Signet.Core.Parsers;

/// <summary>The first matching CSS rule found by <see cref="CssClassDefinitionLocator.Find"/>.</summary>
/// <param name="BookPath">The bookpath of the file containing the rule (a CSS stylesheet or the HTML file itself for an inline style).</param>
/// <param name="Offset">The 0-based offset of the selector in that file.</param>
public readonly record struct CssClassDefinition(string BookPath, int Offset);

/// <summary>
/// Finding the first CSS rule matching a class name for a given (X)HTML file — the logic
/// behind "Jump to CSS class definition" (Ctrl+click on <c>class="..."</c>).
/// </summary>
/// <remarks>
/// The search order: first the stylesheets linked via <c>&lt;link rel="stylesheet"&gt;</c>
/// (in declaration order in <c>&lt;head&gt;</c>, as <see cref="HtmlResource.GetLinkedStylesheets"/>),
/// then the <c>&lt;style&gt;</c> blocks inside the HTML file itself — the same order that
/// <see cref="CssSelectorUsageAnalyzer"/> uses. No attempt is made to reproduce the full CSS cascade (specificity,
/// <c>@media</c>) — the first hit wins, as with "go to definition" in code editors. In each source a selector naming the
/// class on its own (<c>.note</c>, <c>p.note</c>) comes first; otherwise any selector naming it, also before a combinator
/// or with a pseudo-class (<c>div.note p</c>, <c>.note:hover</c>).
/// </remarks>
public static class CssClassDefinitionLocator
{
    /// <summary>
    /// Looks for the first rule with a selector containing the class <paramref name="className"/>.
    /// <paramref name="resolveCssInfo"/> supplies the parsed content of a CSS stylesheet by its bookpath
    /// (e.g. from <c>FolderKeeper</c>) — <c>null</c> when the stylesheet does not exist / cannot be loaded.
    /// </summary>
    public static CssClassDefinition? Find(HtmlResource html, string className, Func<string, CssInfo?> resolveCssInfo)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(resolveCssInfo);

        if (className.Length == 0)
        {
            return null;
        }

        foreach (string cssBookPath in html.GetLinkedStylesheets())
        {
            CssInfo? info = resolveCssInfo(cssBookPath);
            if (info is null)
            {
                continue;
            }

            if (FirstMatch(info, className) is CssSelector match)
            {
                return new CssClassDefinition(cssBookPath, match.Pos);
            }
        }

        HtmlStyleInfo styleInfo = new(html.GetText());
        foreach (CssInfo block in styleInfo.Styles)
        {
            if (FirstMatch(block, className) is CssSelector match)
            {
                return new CssClassDefinition(html.BookPath, match.Pos);
            }
        }

        return null;
    }

    // A simple class selector first; then the first selector naming the class anywhere (CssSelector.ClassName is set
    // only for selectors without a combinator or a pseudo-class).
    private static CssSelector? FirstMatch(CssInfo info, string className)
    {
        IReadOnlyList<CssSelector> simple = info.GetClassSelectors(className);
        if (simple.Count > 0)
        {
            return simple[0];
        }

        foreach (CssSelector selector in info.GetAllSelectors())
        {
            foreach (string name in selector.ClassNames)
            {
                if (string.Equals(name, className, StringComparison.Ordinal))
                {
                    return selector;
                }
            }
        }

        return null;
    }
}
