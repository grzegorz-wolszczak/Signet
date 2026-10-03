using System;
using System.Collections.Generic;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.MiscEditors;

/// <summary>
/// Generates CSS clips from the class selectors used in the book's stylesheets. For a selector
/// starting with a dot (<c>.foo</c>) it creates three variants (<c>p</c>/<c>span</c>/<c>div</c>);
/// for a selector with an element name (e.g. <c>h1.foo</c>) — a single entry.
/// </summary>
public static class ClipAutoFill
{
    /// <summary>
    /// Builds a list of clips of the form <c>&lt;element class="name"&gt;\1&lt;/element&gt;</c> for
    /// every class selector found in the CSS stylesheets of <paramref name="book"/>, in the
    /// <c>Autofill</c> group (fullname <c>Autofill/&lt;selector&gt;</c> — <see cref="ClipEditorModel.AddFullNameEntry"/>
    /// creates/merges the group automatically). Repeated calls merge into a single <c>Autofill</c>
    /// group, consistent with the rest of the model, where group paths are always merged.
    /// </summary>
    public static IReadOnlyList<ClipEntry> BuildAutofillEntries(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var cssClasses = new List<string>();
        foreach (CssResource css in book.GetCssResources())
        {
            CssInfo info = new(css.GetText());
            foreach (CssSelector selector in info.GetClassSelectors())
            {
                string text = selector.Text;
                if (!text.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

                if (text.StartsWith('.'))
                {
                    cssClasses.Add("p" + text);
                    cssClasses.Add("span" + text);
                    cssClasses.Add("div" + text);
                }
                else
                {
                    cssClasses.Add(text);
                }
            }
        }

        var entries = new List<ClipEntry>();
        foreach (string group in cssClasses)
        {
            string[] values = group.Split('.', 2);
            if (values.Length < 2)
            {
                continue;
            }

            string element = values[0];
            string className = values[1];
            string text = "<" + element + " class=\"" + className + "\">" + "\\1" + "</" + element + ">";
            entries.Add(new ClipEntry(false, "Autofill/" + group, group, text));
        }

        return entries;
    }
}
