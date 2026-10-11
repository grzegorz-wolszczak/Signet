using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;

namespace Signet.Core.Footnotes;

/// <summary>What suggests that a file is the book's notes file (<see cref="GlobalFileCandidate.Hints"/>).</summary>
public enum GlobalFileHint
{
    /// <summary>An element of the file has <c>epub:type="endnotes"</c> (or <c>rearnotes</c>, <c>footnotes</c>).</summary>
    NotesType,

    /// <summary>An element of the file has <c>role="doc-endnotes"</c>.</summary>
    NotesRole,

    /// <summary>The landmarks (EPUB 3) or the guide (OPF) point at the file as the notes.</summary>
    Landmark,

    /// <summary>The first heading of the file is "Przypisy", "Notes", "Endnotes"…</summary>
    NotesHeading,
}

/// <summary>
/// A file that holds the notes of global footnotes of other origin — a candidate for the book's notes file when the
/// footnotes are converted (<see cref="FootnoteEditor.GlobalFileCandidates"/>).
/// </summary>
/// <param name="File">The file.</param>
/// <param name="NoteCount">How many notes of global footnotes of other origin it holds.</param>
/// <param name="IsDedicated">
/// Whether it holds nothing but notes: besides them and headings at most <see cref="FootnoteEditor.IntroLimit"/>
/// characters of text (a short introduction).
/// </param>
/// <param name="HoldsAll">Whether it holds the notes of all global footnotes of other origin.</param>
/// <param name="Hints">What else suggests that it is the notes file.</param>
public sealed record GlobalFileCandidate(
    HtmlResource File, int NoteCount, bool IsDedicated, bool HoldsAll, IReadOnlyList<GlobalFileHint> Hints);

/// <content>Choosing the notes file of the global footnotes and the rule for removing it.</content>
public static partial class FootnoteEditor
{
    /// <summary>
    /// How many characters of text besides the notes and headings a dedicated notes file may have
    /// (<see cref="GlobalFileCandidate.IsDedicated"/>).
    /// </summary>
    public const int IntroLimit = 300;

    private static readonly Regex NotesHeadingText = new(
        @"^(przypisy|przypisy końcowe|notes|endnotes|footnotes|notes and references)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] NotesTypes = { "endnotes", "rearnotes", "footnotes" };

    /// <summary>
    /// The files that hold the notes of global footnotes of other origin, in reading order, each with what tells whether
    /// it is the book's notes file.
    /// </summary>
    public static IReadOnlyList<GlobalFileCandidate> GlobalFileCandidates(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        List<Footnote> foreign = FootnoteScanner.Scan(book)
            .Where(f => !f.IsSignet && f.Placement == FootnotePlacement.Global)
            .ToList();
        HtmlResource? nav = book.IsEpub3 ? book.GetNavResource() : null;
        NavProcessor? landmarks = nav is null ? null : new NavProcessor(nav);
        List<GlobalFileCandidate> candidates = new();
        foreach (HtmlResource file in book.GetHtmlResources())
        {
            List<Footnote> notes = foreign.Where(f => ReferenceEquals(f.NoteFile, file)).ToList();
            if (notes.Count == 0)
            {
                continue;
            }

            string text = file.GetText();
            candidates.Add(new GlobalFileCandidate(
                file,
                notes.Count,
                IsDedicated(text, notes.Select(n => n.NoteId).ToHashSet(StringComparer.Ordinal)),
                notes.Count == foreign.Count,
                Hints(book, file, text, landmarks)));
        }

        return candidates;
    }

    /// <summary>
    /// The candidate that becomes the notes file without asking: the only one, dedicated and holding all the global
    /// notes; <c>null</c> when the user has to choose (several candidates, or the only one is not dedicated) or there is
    /// none.
    /// </summary>
    public static GlobalFileCandidate? AutomaticGlobalFile(IReadOnlyList<GlobalFileCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates is [{ IsDedicated: true, HoldsAll: true } only] ? only : null;
    }

    // Besides the notes and the headings, at most IntroLimit characters of text in the body.
    private static bool IsDedicated(string text, HashSet<string> noteIds)
    {
        TagLister tags = new(text);
        List<(int Start, int End)> skipped = new();
        for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin
                && (IsHeading(tag.TagName) || noteIds.Contains(AttributeValue(text, tag, "id")))
                && ToElement(tags, text, i) is { } element)
            {
                skipped.Add((element.Start, element.End));
            }
        }

        int body = tags.FindBodyOpenTag();
        int bodyClose = tags.FindBodyCloseTag();
        if (body < 0 || bodyClose < 0)
        {
            return false;
        }

        int position = tags.At(body).Pos + tags.At(body).Len;
        int end = tags.At(bodyClose).Pos;
        StringBuilder rest = new();
        foreach ((int start, int stop) in skipped.Where(s => s.Start >= position).OrderBy(s => s.Start))
        {
            if (start >= position)
            {
                rest.Append(text, position, Math.Min(start, end) - position).Append(' ');
                position = Math.Max(position, stop);
            }
        }

        if (position < end)
        {
            rest.Append(text, position, end - position);
        }

        return PlainText(rest.ToString()).Length <= IntroLimit;
    }

    private static List<GlobalFileHint> Hints(Book book, HtmlResource file, string text, NavProcessor? landmarks)
    {
        List<GlobalFileHint> hints = new();
        TagLister tags = new(text);
        bool type = false;
        bool role = false;
        for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (!IsOpening(tag))
            {
                continue;
            }

            type |= AttributeValue(text, tag, "epub:type").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(t => NotesTypes.Contains(t, StringComparer.Ordinal));
            role |= AttributeValue(text, tag, "role").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("doc-endnotes", StringComparer.Ordinal);
        }

        if (type)
        {
            hints.Add(GlobalFileHint.NotesType);
        }

        if (role)
        {
            hints.Add(GlobalFileHint.NotesRole);
        }

        string landmark = landmarks?.GetLandmarkCodeForResource(file) ?? string.Empty;
        string guide = book.GetOpf().GetGuideSemanticCodeForResource(file);
        if (NotesTypes.Contains(landmark, StringComparer.Ordinal) || guide == "notes")
        {
            hints.Add(GlobalFileHint.Landmark);
        }

        for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin && IsHeading(tag.TagName) && tags.FindCloseTagForOpen(i) is var close and >= 0)
            {
                if (NotesHeadingText.IsMatch(PlainText(text[(tag.Pos + tag.Len)..tags.At(close).Pos])))
                {
                    hints.Add(GlobalFileHint.NotesHeading);
                }

                break;
            }
        }

        return hints;
    }

    // Whether the global notes file holds nothing besides Signet's empty container and Signet's heading (it can then go
    // with its last note); otherwise only the container goes.
    private static bool HoldsOnlySignetParts(string text, TagLister tags, Element container)
    {
        int body = tags.FindBodyOpenTag();
        int bodyClose = tags.FindBodyCloseTag();
        if (body < 0 || bodyClose < 0)
        {
            return false;
        }

        string inside = text[(tags.At(body).Pos + tags.At(body).Len)..tags.At(bodyClose).Pos];
        string withoutContainer = inside.Replace(text[container.Start..container.End], string.Empty, StringComparison.Ordinal);
        string withoutHeading = Regex.Replace(
            withoutContainer, "<h1>\\s*" + Regex.Escape(GlobalHeading) + "\\s*</h1>", string.Empty, RegexOptions.CultureInvariant);
        string withoutComments = Regex.Replace(withoutHeading, "<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return WebUtility.HtmlDecode(withoutComments).Trim().Length == 0;
    }
}
