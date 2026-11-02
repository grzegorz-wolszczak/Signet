using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Hyphenation;

namespace Signet.Core.BookManipulation;

/// <summary>A soft hyphen outside the text of any language (an attribute value, the head, a <c>pre</c>, a comment…).</summary>
/// <param name="BookPath">The file.</param>
/// <param name="Offset">Where the soft hyphen starts in the source of the file.</param>
/// <param name="Length">Its length in the source (<c>&amp;shy;</c> is 5, the character 1).</param>
/// <param name="Line">Its line (1-based).</param>
/// <param name="Column">Its column (1-based).</param>
/// <param name="Kind">What it stands in.</param>
/// <param name="Name">The attribute or element it stands in (<c>href</c>, <c>pre</c>, <c>head</c>); empty for a comment.</param>
/// <param name="Context">The line around it, for the list.</param>
/// <param name="ContextMatchStart">Where the soft hyphen is in <paramref name="Context"/>.</param>
/// <param name="ContextMatchLength">Its length in <paramref name="Context"/>.</param>
/// <param name="Text">How it is written (<c>&amp;#173;</c>, <c>&amp;shy;</c>, the character…).</param>
public sealed record SoftHyphenPlace(
    string BookPath, int Offset, int Length, int Line, int Column, SoftHyphenPlaceKind Kind, string Name,
    string Context, int ContextMatchStart, int ContextMatchLength, string Text)
{
    /// <summary>Whether it is written in <paramref name="form"/> already.</summary>
    public bool IsIn(SoftHyphenForm form) => SoftHyphenInserter.FormOf(Text) == form;
}

/// <summary>A language in one file: its words and soft hyphens there, and where the file says the language.</summary>
public sealed class SoftHyphenLanguageInFile
{
    /// <summary>Words longer than this are counted together under it (no word of prose is that long).</summary>
    public const int LongestCountedWord = 64;

    private readonly int[] _wordsByLength = new int[LongestCountedWord + 1];
    private readonly int[] _softHyphensByForm = new int[2];
    private readonly HashSet<LanguageSource> _sources = new();

    internal SoftHyphenLanguageInFile(string bookPath)
    {
        BookPath = bookPath;
    }

    /// <summary>The file.</summary>
    public string BookPath { get; }

    /// <summary>The soft hyphens in the text of the language in this file.</summary>
    public int SoftHyphens { get; private set; }

    /// <summary>The soft hyphens in the text of the language in this file not written in <paramref name="form"/>.</summary>
    public int SoftHyphensNotIn(SoftHyphenForm form) => SoftHyphens - _softHyphensByForm[(int)form];

    /// <summary>Where the file says the language (an element with <c>lang</c>, the OPF…).</summary>
    public IReadOnlyCollection<LanguageSource> Sources => _sources;

    /// <summary>
    /// The words with at least <paramref name="minLength"/> letters and no soft hyphen yet — the ones "Add Soft Hyphens"
    /// would look at.
    /// </summary>
    public int WordsToHyphenate(int minLength)
    {
        int sum = 0;
        for (int length = Math.Clamp(minLength, 0, LongestCountedWord); length <= LongestCountedWord; length++)
        {
            sum += _wordsByLength[length];
        }

        return sum;
    }

    internal void AddWord(int letters) => _wordsByLength[Math.Min(letters, LongestCountedWord)]++;

    internal void AddSoftHyphen(string text)
    {
        SoftHyphens++;
        if (SoftHyphenInserter.FormOf(text) is { } form)
        {
            _softHyphensByForm[(int)form]++;
        }
    }

    internal void AddSource(LanguageSource source) => _sources.Add(source);
}

/// <summary>
/// A language of the book (as the elements and the OPF say it), with the files its text is in. The key compares
/// languages without regard to case and with <c>_</c> read as <c>-</c>; the unknown language (no element and no OPF
/// says one) has the empty key.
/// </summary>
public sealed class SoftHyphenLanguage
{
    internal SoftHyphenLanguage(string language, IReadOnlyList<SoftHyphenLanguageInFile> files)
    {
        Language = language;
        Key = KeyOf(language);
        Files = files;
    }

    /// <summary>The language as the book first says it (<c>pl</c>, <c>en-GB</c>); empty = unknown.</summary>
    public string Language { get; }

    /// <summary>The key of the language (<see cref="KeyOf"/>).</summary>
    public string Key { get; }

    /// <summary>Whether no element and no OPF says the language of this text.</summary>
    public bool IsUnknown => Key.Length == 0;

    /// <summary>The files with text in the language, in the order of the book.</summary>
    public IReadOnlyList<SoftHyphenLanguageInFile> Files { get; }

    /// <summary>The soft hyphens in the text of the language.</summary>
    public int SoftHyphens => Files.Sum(f => f.SoftHyphens);

    /// <summary>The soft hyphens in the text of the language not written in <paramref name="form"/>.</summary>
    public int SoftHyphensNotIn(SoftHyphenForm form) => Files.Sum(f => f.SoftHyphensNotIn(form));

    /// <summary>Where the book says the language, and in how many files each.</summary>
    public IReadOnlyList<(LanguageSource Source, int Files)> Sources =>
        Files.SelectMany(f => f.Sources)
            .GroupBy(s => s)
            .Select(g => (g.Key, g.Count()))
            .OrderBy(s => s.Key.Kind)
            .ThenByDescending(s => s.Item2)
            .ThenBy(s => s.Key.ElementName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The words "Add Soft Hyphens" would look at (<see cref="SoftHyphenLanguageInFile.WordsToHyphenate"/>).</summary>
    public int WordsToHyphenate(int minLength) => Files.Sum(f => f.WordsToHyphenate(minLength));

    /// <summary>How languages are compared: <c>_</c> read as <c>-</c>, no case.</summary>
    public static string KeyOf(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return HyphenationPatternFiles.Normalize(language).ToLowerInvariant();
    }
}

/// <summary>
/// What "Add / Remove Soft Hyphens" see in a book: the languages of its text, file by file, with their words and soft
/// hyphens, and the soft hyphens outside the text of any language, one by one.
/// </summary>
public sealed class SoftHyphenSurvey
{
    private SoftHyphenSurvey(IReadOnlyList<SoftHyphenLanguage> languages, IReadOnlyList<SoftHyphenPlace> outsideText)
    {
        Languages = languages;
        OutsideText = outsideText;
    }

    /// <summary>The languages of the text, in the order they first appear (the unknown one, if any, last).</summary>
    public IReadOnlyList<SoftHyphenLanguage> Languages { get; }

    /// <summary>The soft hyphens outside the text of any language, file by file in text order.</summary>
    public IReadOnlyList<SoftHyphenPlace> OutsideText { get; }

    /// <summary>Whether the book has a soft hyphen anywhere.</summary>
    public bool HasSoftHyphens => OutsideText.Count > 0 || Languages.Any(l => l.SoftHyphens > 0);

    /// <summary>Surveys the files of a book.</summary>
    /// <param name="files">The XHTML files: their book path and source, in the order of the book.</param>
    /// <param name="defaultLanguage">The language of text no element states one for (the OPF's first <c>dc:language</c>); empty = unknown.</param>
    public static SoftHyphenSurvey Of(IEnumerable<(string BookPath, string Text)> files, string defaultLanguage)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(defaultLanguage);

        Dictionary<string, string> display = new(StringComparer.Ordinal);
        Dictionary<string, List<SoftHyphenLanguageInFile>> byLanguage = new(StringComparer.Ordinal);
        List<string> order = new();
        List<SoftHyphenPlace> outside = new();

        foreach ((string bookPath, string text) in files)
        {
            Dictionary<string, SoftHyphenLanguageInFile> inFile = new(StringComparer.Ordinal);
            ClassUsageFinder.LineIndex? lines = null;
            foreach (SoftHyphenSegment segment in SoftHyphenScanner.Scan(text, defaultLanguage))
            {
                if (!segment.IsText)
                {
                    foreach ((int offset, int length) in SoftHyphenScanner.SoftHyphens(text, segment.Start, segment.End))
                    {
                        lines ??= new ClassUsageFinder.LineIndex(text);
                        (int line, int column) = lines.LocationOf(offset);
                        (string context, int matchStart, int matchLength) = ClassUsageFinder.Context(text, offset, column, length);
                        outside.Add(new SoftHyphenPlace(
                            bookPath, offset, length, line, column, segment.Kind, segment.Name, context, matchStart, matchLength,
                            text.Substring(offset, length)));
                    }

                    continue;
                }

                string key = SoftHyphenLanguage.KeyOf(segment.Language);
                if (!inFile.TryGetValue(key, out SoftHyphenLanguageInFile? usage))
                {
                    usage = new SoftHyphenLanguageInFile(bookPath);
                    inFile[key] = usage;
                    if (!byLanguage.TryGetValue(key, out List<SoftHyphenLanguageInFile>? list))
                    {
                        list = new List<SoftHyphenLanguageInFile>();
                        byLanguage[key] = list;
                        display[key] = HyphenationPatternFiles.Normalize(segment.Language);
                        order.Add(key);
                    }

                    list.Add(usage);
                }

                bool hasLetters = false;
                foreach (SoftHyphenWord word in SoftHyphenScanner.Words(text, segment))
                {
                    hasLetters = true;
                    if (!word.HasSoftHyphen)
                    {
                        usage.AddWord(word.Letters);
                    }
                }

                foreach ((int offset, int length) in SoftHyphenScanner.SoftHyphens(text, segment.Start, segment.End))
                {
                    usage.AddSoftHyphen(text.Substring(offset, length));
                }

                if (hasLetters)
                {
                    usage.AddSource(segment.Source);
                }
            }
        }

        // A language whose text has no letter anywhere (only white space between tags) is no language of the book.
        List<SoftHyphenLanguage> languages = order
            .Select(key => (Key: key, Files: byLanguage[key].Where(f => f.Sources.Count > 0 || f.SoftHyphens > 0).ToList()))
            .Where(l => l.Files.Count > 0)
            .OrderBy(l => l.Key.Length == 0)
            .Select(l => new SoftHyphenLanguage(display[l.Key], l.Files))
            .ToList();
        return new SoftHyphenSurvey(languages, outside);
    }
}
