using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.MainUI;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>What "Remove span" in Code View removes.</summary>
public enum SpanRemovalScope
{
    /// <summary>The span under the caret.</summary>
    This,

    /// <summary>Every span of the file that is the same as the one under the caret.</summary>
    File,

    /// <summary>Every span of the book that is the same as the one under the caret.</summary>
    Book,
}

/// <summary>
/// A planned "Remove span" (<see cref="SpanRemoval.Plan"/>): the new texts of the changed files and what the
/// removal would change in the book's styling — shown to the user for acceptance when not empty.
/// </summary>
/// <param name="Scope">What is removed.</param>
/// <param name="NewTexts">The new text of every changed (X)HTML file, by book path.</param>
/// <param name="SpanCount">How many spans are removed.</param>
/// <param name="Consequences">What changes in the styling (<see cref="SpanRemovalRiskAnalyzer"/>); empty when safe.</param>
public sealed record SpanRemovalPlan(
    SpanRemovalScope Scope,
    IReadOnlyDictionary<string, string> NewTexts,
    int SpanCount,
    IReadOnlyList<CleanupConsequence> Consequences);

/// <summary>
/// "Remove span" (Code View context menu) on the level of the book: finds the spans to remove (the one under the
/// caret, or every span of the file / the book that is the same as it — <see cref="SpanUnwrapper"/>), removes their
/// tags and keeps their content, and finds out what that changes in the styling, so the user can accept it first.
/// </summary>
public static class SpanRemoval
{
    /// <summary>
    /// How many spans of the book are the same as the span under <paramref name="offset"/> in
    /// <paramref name="text"/> (the current text of <paramref name="html"/>); 0 when there is none or it has an id.
    /// </summary>
    public static int CountInBook(Book book, HtmlResource html, string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        if (SpanUnwrapper.SpansAt(text, offset, all: false).Count == 0 || SpanUnwrapper.OpenTagAt(text, offset) is not { } openTag)
        {
            return 0;
        }

        return Texts(book, html, text).Sum(t => SpanUnwrapper.SameAs(t.Text, openTag).Count);
    }

    /// <summary>
    /// Plans the removal of <paramref name="scope"/> for the span under <paramref name="offset"/> in
    /// <paramref name="text"/> (the current text of <paramref name="html"/>; the other files are taken from the book).
    /// <c>null</c> when there is no span there or it has an <c>id</c>.
    /// </summary>
    public static SpanRemovalPlan? Plan(Book book, HtmlResource html, string text, int offset, SpanRemovalScope scope)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        if (SpanUnwrapper.SpansAt(text, offset, all: false).Count == 0 || SpanUnwrapper.OpenTagAt(text, offset) is not { } openTag)
        {
            return null;
        }

        IEnumerable<(HtmlResource Resource, string Text)> files = scope == SpanRemovalScope.Book
            ? Texts(book, html, text)
            : new[] { (html, text) };

        Dictionary<string, string> newTexts = new(StringComparer.Ordinal);
        List<CleanupConsequence> consequences = new();
        int count = 0;
        foreach ((HtmlResource resource, string fileText) in files)
        {
            IReadOnlyList<SpanTags> spans = scope == SpanRemovalScope.This
                ? SpanUnwrapper.SpansAt(fileText, offset, all: false)
                : SpanUnwrapper.SameAs(fileText, openTag);
            if (spans.Count == 0)
            {
                continue;
            }

            count += spans.Count;
            newTexts[resource.BookPath] = BareSpanCleaner.RemoveTags(fileText, spans);
            consequences.AddRange(SpanRemovalRiskAnalyzer.AnalyseTogether(resource.BookPath, fileText, spans, Stylesheets(book, resource)));
        }

        return new SpanRemovalPlan(scope, newTexts, count, consequences);
    }

    // Every (X)HTML file of the book with its text — the current text for "html".
    private static IEnumerable<(HtmlResource Resource, string Text)> Texts(Book book, HtmlResource html, string text) =>
        book.GetHtmlResources().Select(r => (r, ReferenceEquals(r, html) ? text : r.GetText()));

    private static List<CssInfo> Stylesheets(Book book, HtmlResource html)
    {
        Dictionary<string, CssResource> byPath = book.GetCssResources().ToDictionary(c => c.BookPath, StringComparer.Ordinal);
        return book.GetVisibleStylesheets(html)
            .Where(byPath.ContainsKey)
            .Select(path => new CssInfo(byPath[path].GetText()))
            .ToList();
    }
}
