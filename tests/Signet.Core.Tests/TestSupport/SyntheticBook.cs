using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Which footnotes a <see cref="SyntheticBook"/> is written with.</summary>
public enum SyntheticNotes
{
    /// <summary>No footnotes at all.</summary>
    None,

    /// <summary>Footnotes of other origin, each note in the file of its reference — what a conversion is given.</summary>
    Foreign,

    /// <summary>Footnotes of other origin with every note in one notes file, the references spread over the book.</summary>
    ForeignInOneFile,

    /// <summary>Signet's own footnotes, each note in the file of its reference.</summary>
    Signet,

    /// <summary>Signet's own footnotes with every note in one notes file — what Renumber and Change Placement meet.</summary>
    SignetInOneFile,
}

/// <summary>
/// Writes an EPUB of a given size, for the operations whose cost grows with the book.
/// </summary>
/// <remarks>
/// <para>
/// The books of <c>tests/corpus</c> hold one to three content files, which is enough for what an operation does but
/// not for what it costs: every quadratic found so far in the footnote code showed itself only at a thousand files or
/// at thousands of notes in one file, and none of them could have been reproduced on the corpus. A book of that size
/// is generated rather than kept in the repository — what matters here is the scale, not the variety of the markup,
/// and a thousand files of Lorem ipsum would weigh more than they teach. For markup as messy as a real book's there
/// are the books under <c>tests/corpus/real</c>.
/// </para>
/// <para>
/// The output is the same for the same arguments, down to the ids and the text, so a measurement can be repeated and
/// a failure can be looked at again.
/// </para>
/// </remarks>
public static class SyntheticBook
{
    private const string NotesFile = "notes.xhtml";

    private static readonly string[] Lorem =
    {
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
        "Duis aute irure dolor in reprehenderit in voluptate velit esse.",
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia.",
    };

    /// <summary>Writes the unpacked book into <paramref name="root"/> and returns that path.</summary>
    /// <param name="root">The directory to write into; it is created if it is not there.</param>
    /// <param name="files">How many content files the book has (at least one).</param>
    /// <param name="notesPerFile">How many footnotes each content file holds.</param>
    /// <param name="notes">Which footnotes to write.</param>
    public static string BuildTree(string root, int files, int notesPerFile = 0, SyntheticNotes notes = SyntheticNotes.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(files, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(notesPerFile);

        bool oneFile = notes is SyntheticNotes.ForeignInOneFile or SyntheticNotes.SignetInOneFile;
        bool signet = notes is SyntheticNotes.Signet or SyntheticNotes.SignetInOneFile;
        int perFile = notes == SyntheticNotes.None ? 0 : notesPerFile;

        Directory.CreateDirectory(Path.Combine(root, "META-INF"));
        Directory.CreateDirectory(Path.Combine(root, "EPUB", "text"));
        Directory.CreateDirectory(Path.Combine(root, "EPUB", "styles"));
        Write(Path.Combine(root, "mimetype"), "application/epub+zip");
        Write(Path.Combine(root, "META-INF", "container.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n"
            + "  <rootfiles>\n"
            + "    <rootfile full-path=\"EPUB/package.opf\" media-type=\"application/oebps-package+xml\"/>\n"
            + "  </rootfiles>\n</container>\n");
        Write(Path.Combine(root, "EPUB", "styles", "style.css"), "body { margin: 1em; }\n");

        List<string> chapters = new(files);
        for (int file = 0; file < files; file++)
        {
            chapters.Add("chapter" + Name(file) + ".xhtml");
        }

        // The notes of a one-file book wait here and go into the notes file at the end.
        StringBuilder collected = new();
        for (int file = 0; file < files; file++)
        {
            StringBuilder body = new();
            StringBuilder here = new();
            body.Append(CultureInfo.InvariantCulture, $"  <h1 id=\"ch{Name(file)}\">Chapter {file + 1}</h1>\n");
            for (int i = 0; i < perFile; i++)
            {
                string id = Name(file) + "-" + Name(i);
                string noteHref = oneFile ? "../" + NotesFile + "#fn-" + id : "#fn-" + id;
                body.Append(CultureInfo.InvariantCulture,
                    $"  <p>{Lorem[(file + i) % Lorem.Length]}{Reference(signet, noteHref, "ref-" + id, i + 1)}</p>\n");
                string back = oneFile ? "text/" + chapters[file] + "#ref-" + id : "#ref-" + id;
                (oneFile ? collected : here).Append(Note(signet, "fn-" + id, back, i + 1, Lorem[(file + i + 2) % Lorem.Length]));
            }

            if (here.Length > 0)
            {
                body.Append(OpenContainer(signet)).Append(here).Append(CloseContainer);
            }

            Write(Path.Combine(root, "EPUB", "text", chapters[file]), Page("Chapter " + (file + 1), "../styles/style.css", body.ToString()));
        }

        if (oneFile && perFile > 0)
        {
            Write(
                Path.Combine(root, "EPUB", NotesFile),
                Page("Notes", "styles/style.css", "  <h1>Notes</h1>\n" + OpenContainer(signet) + collected + CloseContainer));
        }

        Write(Path.Combine(root, "EPUB", "nav.xhtml"), Nav(chapters));
        Write(Path.Combine(root, "EPUB", "package.opf"), Opf(chapters, oneFile && perFile > 0));
        return root;
    }

    /// <summary>Writes the book into <paramref name="temp"/> and packs it; returns the <c>.epub</c> path.</summary>
    public static string BuildEpub(
        TempDir temp, int files, int notesPerFile = 0, SyntheticNotes notes = SyntheticNotes.None, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(temp);
        string tree = BuildTree(temp.Combine("tree-" + files + "-" + notesPerFile + "-" + notes), files, notesPerFile, notes);
        return EpubBuilder.BuildInto(tree, temp, name ?? "synthetic-" + files + "-" + notesPerFile + ".epub");
    }

    // A fixed-width name, so the files sort the way they are written.
    private static string Name(int i) => i.ToString("D5", CultureInfo.InvariantCulture);

    private static string Reference(bool signet, string href, string id, int number) =>
        signet
            ? "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + href + "\" id=\"" + id + "\">["
              + number.ToString(CultureInfo.InvariantCulture) + "]</a>"
            : "<sup><a epub:type=\"noteref\" href=\"" + href + "\" id=\"" + id + "\">["
              + number.ToString(CultureInfo.InvariantCulture) + "]</a></sup>";

    private static string Note(bool signet, string id, string back, int number, string text) =>
        signet
            ? "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"" + id + "\"><p class=\"signet-fn-p\"><a href=\"" + back + "\">"
              + number.ToString(CultureInfo.InvariantCulture) + ".</a> " + text + "</p></aside>\n"
            : "  <aside epub:type=\"footnote\" id=\"" + id + "\"><p><a href=\"" + back + "\">"
              + number.ToString(CultureInfo.InvariantCulture) + ".</a> " + text + "</p></aside>\n";

    private static string OpenContainer(bool signet) =>
        signet
            ? "  <section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
            : "  <section epub:type=\"footnotes\">\n";

    private const string CloseContainer = "  </section>\n";

    private static string Page(string title, string css, string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n"
        + "<head>\n  <title>" + title + "</title>\n  <meta charset=\"utf-8\"/>\n"
        + "  <link rel=\"stylesheet\" type=\"text/css\" href=\"" + css + "\"/>\n</head>\n<body>\n"
        + body + "</body>\n</html>\n";

    private static string Nav(List<string> chapters)
    {
        StringBuilder list = new();
        for (int i = 0; i < chapters.Count; i++)
        {
            list.Append(CultureInfo.InvariantCulture, $"      <li><a href=\"text/{chapters[i]}\">Chapter {i + 1}</a></li>\n");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n"
            + "<head>\n  <title>Table of Contents</title>\n  <meta charset=\"utf-8\"/>\n</head>\n<body>\n"
            + "  <nav epub:type=\"toc\" id=\"toc\">\n    <h1>Table of Contents</h1>\n    <ol>\n"
            + list + "    </ol>\n  </nav>\n</body>\n</html>\n";
    }

    private static string Opf(List<string> chapters, bool withNotesFile)
    {
        StringBuilder manifest = new();
        StringBuilder spine = new();
        for (int i = 0; i < chapters.Count; i++)
        {
            manifest.Append(CultureInfo.InvariantCulture,
                $"    <item id=\"ch{Name(i)}\" href=\"text/{chapters[i]}\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append(CultureInfo.InvariantCulture, $"    <itemref idref=\"ch{Name(i)}\"/>\n");
        }

        if (withNotesFile)
        {
            manifest.Append(CultureInfo.InvariantCulture,
                $"    <item id=\"notes\" href=\"{NotesFile}\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append("    <itemref idref=\"notes\"/>\n");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"pub-id\" xml:lang=\"en\">\n"
            + "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n"
            + "    <dc:identifier id=\"pub-id\">urn:uuid:22222222-2222-4222-8222-222222222222</dc:identifier>\n"
            + "    <dc:title>Synthetic</dc:title>\n    <dc:language>en</dc:language>\n"
            + "    <meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>\n  </metadata>\n"
            + "  <manifest>\n    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n"
            + "    <item id=\"css\" href=\"styles/style.css\" media-type=\"text/css\"/>\n"
            + manifest + "  </manifest>\n  <spine>\n" + spine + "  </spine>\n</package>\n";
    }

    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
}
