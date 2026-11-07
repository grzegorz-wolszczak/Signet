using System;
using System.Collections.Generic;
using System.Net;
using Signet.Core.Localization;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks the <c>id</c> attributes of every (X)HTML file: an id must be a valid XML name without a colon (NCName), which
/// EPUBCheck does not require (it only rejects ids with white space) but the XML tools and some readers do. The problem has an
/// automatic fix (<see cref="InvalidIdFix"/>). Called from <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// An id that is not unique within its file is reported by EPUBCheck, which also brings the fix (<see cref="DuplicateIdFix"/>,
/// see <see cref="EpubCheckFixes"/>). An id shared by several files is legal; that case is reported (as a warning, without
/// a fix) by <see cref="CrossFileStructureValidator"/>.
/// </remarks>
public static class IdValidator
{
    /// <summary>Runs the id checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            CheckFile(html, results);
        }

        return results;
    }

    private static void CheckFile(HtmlResource html, List<ValidationResult> results)
    {
        string text = html.GetText();

        // id -> (the position of its first element, the number of elements), in order of first occurrence.
        Dictionary<string, (int Position, int Count)> ids = new(StringComparer.Ordinal);
        List<string> order = new();
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            TagLister.AttInfo info = TagLister.ParseAttribute(text.Substring(pos, len), "id");
            if (info.Pos < 0)
            {
                continue;
            }

            string id = WebUtility.HtmlDecode(info.AValue);
            if (ids.TryGetValue(id, out (int Position, int Count) seen))
            {
                ids[id] = (seen.Position, seen.Count + 1);
            }
            else
            {
                ids[id] = (pos, 1);
                order.Add(id);
            }
        }

        foreach (string id in order)
        {
            if (!MarkupEdits.IsValidId(id))
            {
                // The line is counted only for an id that is reported: counting it for each of thousands of ids walks the text from its start every time.
                (int position, _) = ids[id];
                int line = LineOf(text, position);
                results.Add(new ValidationResult(
                    ValidationSeverity.Error, html.BookPath, line, -1,
                    CoreStrings.Format("Validation_InvalidId", id), "Validation_InvalidId", new InvalidIdFix(html.BookPath, id)));
            }
        }
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
