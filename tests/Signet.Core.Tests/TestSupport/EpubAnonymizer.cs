using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Replaces the prose of an EPUB with Lorem ipsum and leaves everything else exactly as it was.
/// </summary>
/// <remarks>
/// <para>
/// A real book teaches what a generated one cannot: the markup a publishing tool actually emits, with its wrappers,
/// its shared notes, its deep folder names and its oddities. The text of such a book is someone's work, so a fixture
/// made from it keeps the structure and gives up the words.
/// </para>
/// <para>
/// What is kept, because it is what the tests are about: every tag and attribute (ids, hrefs, classes,
/// <c>epub:type</c>, languages), every entity reference, every digit and every piece of punctuation — so footnote
/// markers like <c>[12]</c>, verse numbers and link targets survive untouched. What is replaced: runs of letters in
/// text content, each by a Lorem word of the same length, which keeps the shape and the size of the file. A token
/// that looks like an identifier or an address (it holds <c>:</c>, <c>/</c>, <c>#</c> or <c>@</c>) is left alone, so
/// <c>urn:uuid:…</c> in the OPF stays a URN.
/// </para>
/// <para>
/// Letters outside ASCII are replaced too, so the Polish of the source leaves no trace; that also means a fixture
/// made this way carries no diacritics, and is not the book to test spelling on.
/// </para>
/// </remarks>
public static class EpubAnonymizer
{
    private static readonly string[] Words =
    {
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do",
        "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim",
        "ad", "minim", "veniam", "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi", "aliquip",
    };

    /// <summary>The text of one file with its prose replaced.</summary>
    public static string Anonymize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder result = new(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            if (text[at] == '<')
            {
                int end = SkipMarkup(text, at);
                result.Append(text, at, end - at);
                at = end;
                continue;
            }

            int next = text.IndexOf('<', at);
            int stop = next < 0 ? text.Length : next;
            result.Append(AnonymizeRun(text.AsSpan(at, stop - at)));
            at = stop;
        }

        return result.ToString();
    }

    /// <summary>
    /// Copies the unpacked book at <paramref name="source"/> to <paramref name="destination"/> with its prose
    /// replaced, keeping only the content files that <paramref name="keepContentFile"/> accepts.
    /// </summary>
    /// <param name="source">The unpacked book to read.</param>
    /// <param name="destination">The directory to write; it is created and must be empty or absent.</param>
    /// <param name="keepContentFile">
    /// Asked for the path of every <c>.xhtml</c>/<c>.html</c> file, relative to <paramref name="source"/> with
    /// <c>/</c> separators. A file it turns down is left out, and the OPF manifest, the OPF spine and the navigation
    /// document lose their entries for it. The navigation document and the file the OPF names as the nav are always
    /// kept.
    /// </param>
    public static void AnonymizeTree(string source, string destination, Func<string, bool>? keepContentFile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }

        List<string> all = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(source, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // Which content files go, so that the OPF and the nav can be pruned to match.
        HashSet<string> dropped = new(StringComparer.Ordinal);
        if (keepContentFile is not null)
        {
            foreach (string file in all.Where(IsMarkup).Where(f => !IsNav(f) && !keepContentFile(f)))
            {
                dropped.Add(file);
            }
        }

        foreach (string file in all)
        {
            if (dropped.Contains(file))
            {
                continue;
            }

            string target = Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!IsText(file))
            {
                File.Copy(Path.Combine(source, file), target, overwrite: true);
                continue;
            }

            string text = File.ReadAllText(Path.Combine(source, file));
            if (file.EndsWith("mimetype", StringComparison.Ordinal) || file.EndsWith("container.xml", StringComparison.Ordinal))
            {
                File.WriteAllText(target, text, new UTF8Encoding(false));
                continue;
            }

            if (dropped.Count > 0)
            {
                text = IsNav(file) ? PruneNav(text, Kept(all, dropped), file) : Prune(text, dropped);
            }

            File.WriteAllText(target, Anonymize(text), new UTF8Encoding(false));
        }
    }

    private static bool IsText(string file) =>
        file.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".opf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith("mimetype", StringComparison.Ordinal);

    private static bool IsMarkup(string file) =>
        file.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    // The navigation document is recognized by its name, which is what every book this is used on calls it; a book
    // that named it otherwise would have to say so through `keepContentFile`.
    private static bool IsNav(string file) =>
        file.EndsWith("nav.xhtml", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith("toc.xhtml", StringComparison.OrdinalIgnoreCase);

    private static List<string> Kept(List<string> all, HashSet<string> dropped) =>
        all.Where(IsMarkup).Where(f => !dropped.Contains(f) && !IsNav(f)).ToList();

    // Takes out the manifest items and the spine itemrefs of the dropped files. The OPF of a generated book keeps one
    // entry per line, so a line is the unit; a line that mentions nothing dropped is kept as it is.
    private static string Prune(string text, HashSet<string> dropped)
    {
        HashSet<string> names = dropped.Select(d => d[(d.LastIndexOf('/') + 1)..]).ToHashSet(StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        List<string> kept = new();
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("<item ", StringComparison.Ordinal) && Mentions(line, names))
            {
                if (Regex.Match(line, "\\bid=\"([^\"]+)\"", RegexOptions.CultureInvariant) is { Success: true } id)
                {
                    ids.Add(id.Groups[1].Value);
                }

                continue;
            }

            kept.Add(line);
        }

        List<string> result = new();
        foreach (string line in kept)
        {
            bool isSpine = line.Contains("<itemref ", StringComparison.Ordinal)
                && Regex.Match(line, "\\bidref=\"([^\"]+)\"", RegexOptions.CultureInvariant) is { Success: true } reference
                && ids.Contains(reference.Groups[1].Value);
            if (!isSpine)
            {
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    // The navigation document gets a flat list of the files that stayed. Striking its entries out line by line does
    // not work: a table of contents nests, so the line naming a part also opens an <li> whose </li> is further down,
    // and removing it leaves the document malformed. The shape of the list is not what a fixture made this way is
    // for -- the chapters are -- so a correct flat list is worth more than a faithful broken one.
    private static string PruneNav(string text, List<string> kept, string navFile)
    {
        int list = text.IndexOf("<ol", StringComparison.Ordinal);
        int close = text.LastIndexOf("</ol>", StringComparison.Ordinal);
        if (list < 0 || close < list)
        {
            return text;
        }

        string folder = navFile.Contains('/', StringComparison.Ordinal) ? navFile[..(navFile.LastIndexOf('/') + 1)] : string.Empty;
        StringBuilder items = new("<ol>\n");
        foreach (string file in kept)
        {
            string href = file.StartsWith(folder, StringComparison.Ordinal) ? file[folder.Length..] : file;
            items.Append(CultureInfo.InvariantCulture, $"      <li><a href=\"{href}\">{Title(href)}</a></li>\n");
        }

        return text[..list] + items.Append("    ").ToString() + text[close..];
    }

    // A name for the navigation entry, made from the file name so that it says nothing about the book.
    private static string Title(string href) =>
        href[(href.LastIndexOf('/') + 1)..].Replace(".xhtml", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool Mentions(string line, HashSet<string> names) =>
        names.Any(n => line.Contains("\"" + n, StringComparison.Ordinal) || line.Contains("/" + n, StringComparison.Ordinal));

    // Past a tag, a comment, a CDATA section or a doctype, none of which is prose.
    private static int SkipMarkup(string text, int at)
    {
        if (text.AsSpan(at).StartsWith("<!--", StringComparison.Ordinal))
        {
            int close = text.IndexOf("-->", at, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 3;
        }

        if (text.AsSpan(at).StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            int close = text.IndexOf("]]>", at, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 3;
        }

        int end = text.IndexOf('>', at);
        return end < 0 ? text.Length : end + 1;
    }

    // One run of text between two tags: the letters of a word become Lorem of the same length, everything else stays.
    private static string AnonymizeRun(ReadOnlySpan<char> run)
    {
        StringBuilder result = new(run.Length);
        int at = 0;
        while (at < run.Length)
        {
            if (char.IsWhiteSpace(run[at]))
            {
                result.Append(run[at++]);
                continue;
            }

            int end = at;
            while (end < run.Length && !char.IsWhiteSpace(run[end]))
            {
                end++;
            }

            ReadOnlySpan<char> token = run[at..end];
            result.Append(IsKept(token) ? token : AnonymizeToken(token, result.Length));
            at = end;
        }

        return result.ToString();
    }

    // An address or an identifier is left alone, so urn:uuid:… stays a URN and a path stays a path.
    private static bool IsKept(ReadOnlySpan<char> token) => token.IndexOfAny(IdentifierMarks) >= 0;

    private static readonly char[] IdentifierMarks = { ':', '/', '#', '@' };

    private static string AnonymizeToken(ReadOnlySpan<char> token, int seed)
    {
        StringBuilder result = new(token.Length);
        int at = 0;
        while (at < token.Length)
        {
            if (token[at] == '&')
            {
                int close = token[at..].IndexOf(';');
                if (close > 0 && close <= 10)
                {
                    result.Append(token[at..(at + close + 1)]);
                    at += close + 1;
                    continue;
                }
            }

            if (!char.IsLetter(token[at]))
            {
                result.Append(token[at++]);
                continue;
            }

            int end = at;
            while (end < token.Length && char.IsLetter(token[end]))
            {
                end++;
            }

            result.Append(Lorem(end - at, seed + at, char.IsUpper(token[at])));
            at = end;
        }

        return result.ToString();
    }

    // A Lorem word of exactly `length` letters, the same one for the same length and place.
    private static string Lorem(int length, int seed, bool capital)
    {
        StringBuilder word = new(length);
        int pick = ((seed * 31) + length) % Words.Length;
        while (word.Length < length)
        {
            string next = Words[pick];
            word.Append(next, 0, Math.Min(next.Length, length - word.Length));
            pick = (pick + 7) % Words.Length;
        }

        if (capital)
        {
            word[0] = char.ToUpper(word[0], CultureInfo.InvariantCulture);
        }

        return word.ToString();
    }
}
