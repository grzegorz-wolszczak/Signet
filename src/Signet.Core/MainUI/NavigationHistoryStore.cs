using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Signet.Core.Misc;

namespace Signet.Core.MainUI;

/// <summary>
/// Keeps the <see cref="NavigationHistory"/> of saved books between sessions in one XML file, per EPUB file path:
/// <code>
/// &lt;navigation_history&gt;
///   &lt;book file="/books/a.epub"&gt;
///     &lt;back&gt;&lt;place book_path="OEBPS/Text/c1.xhtml" offset="120" line="7" time="2026-10-14T12:00:00.0000000+02:00" /&gt;&lt;/back&gt;
///     &lt;forward /&gt;
///     &lt;edited /&gt;
///   &lt;/book&gt;
/// &lt;/navigation_history&gt;
/// </code>
/// Places older than the given number of days are dropped on every load and save (a book left without places is
/// removed from the file), so the file does not grow with every book ever edited.
/// </summary>
public sealed class NavigationHistoryStore
{
    /// <summary>The longest time (days) a place is kept.</summary>
    public const int MaxDays = 14;

    private const string RootElement = "navigation_history";
    private const string BookElement = "book";
    private const string FileAttribute = "file";
    private const string BackElement = "back";
    private const string ForwardElement = "forward";
    private const string EditedElement = "edited";
    private const string PlaceElement = "place";

    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates a store on the given XML file.</summary>
    /// <param name="filePath">The file holding the histories.</param>
    /// <param name="clock">The current time (for expiry); <see cref="DateTimeOffset.Now"/> by default.</param>
    public NavigationHistoryStore(string filePath, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _path = filePath;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// The saved history of the book at <paramref name="bookFilePath"/> without the places older than
    /// <paramref name="maxAgeDays"/> days; <c>null</c> when nothing is saved (or the file cannot be read).
    /// </summary>
    public NavigationHistoryState? Load(string bookFilePath, int maxAgeDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookFilePath);
        Dictionary<string, BookEntry> books = Prune(Read(), maxAgeDays);
        return books.TryGetValue(Key(bookFilePath), out BookEntry? entry)
            ? new NavigationHistoryState(ToPlaces(entry.Back), ToPlaces(entry.Forward), ToPlaces(entry.Edited))
            : null;
    }

    /// <summary>
    /// Saves the history of the book at <paramref name="bookFilePath"/> (an empty one removes the book) and drops the
    /// places of all books that are older than <paramref name="maxAgeDays"/> days.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public void Save(string bookFilePath, NavigationHistoryState state, int maxAgeDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookFilePath);
        ArgumentNullException.ThrowIfNull(state);
        Dictionary<string, BookEntry> books = Read();
        books[Key(bookFilePath)] = new BookEntry
        {
            Back = ToEntries(state.Back),
            Forward = ToEntries(state.Forward),
            Edited = ToEntries(state.Edited),
        };
        Write(Prune(books, maxAgeDays));
    }

    // Windows paths are case-insensitive; the full path makes "a\..\b.epub" and "b.epub" one book.
    private static string Key(string bookFilePath)
    {
        string full = Path.GetFullPath(bookFilePath);
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private Dictionary<string, BookEntry> Prune(Dictionary<string, BookEntry> books, int maxAgeDays)
    {
        DateTimeOffset oldest = _clock() - TimeSpan.FromDays(Math.Clamp(maxAgeDays, 1, MaxDays));
        Dictionary<string, BookEntry> kept = new(StringComparer.Ordinal);
        foreach ((string key, BookEntry entry) in books)
        {
            BookEntry pruned = new()
            {
                Back = entry.Back.Where(p => p.Time >= oldest).ToList(),
                Forward = entry.Forward.Where(p => p.Time >= oldest).ToList(),
                Edited = entry.Edited.Where(p => p.Time >= oldest).ToList(),
            };
            if (pruned.Back.Count > 0 || pruned.Forward.Count > 0 || pruned.Edited.Count > 0)
            {
                kept[key] = pruned;
            }
        }

        return kept;
    }

    private Dictionary<string, BookEntry> Read()
    {
        // An unreadable file only costs the saved history.
        Dictionary<string, BookEntry> books = new(StringComparer.Ordinal);
        if (XmlConfig.TryLoad(_path)?.Root is not { } root || root.Name != RootElement)
        {
            return books;
        }

        foreach (XElement book in root.Elements(BookElement))
        {
            if (book.Attribute(FileAttribute)?.Value is { Length: > 0 } key)
            {
                books[key] = new BookEntry
                {
                    Back = ReadPlaces(book.Element(BackElement)),
                    Forward = ReadPlaces(book.Element(ForwardElement)),
                    Edited = ReadPlaces(book.Element(EditedElement)),
                };
            }
        }

        return books;
    }

    private void Write(Dictionary<string, BookEntry> books)
    {
        XElement root = new(
            RootElement,
            books.Select(pair => new XElement(
                BookElement,
                new XAttribute(FileAttribute, pair.Key),
                WritePlaces(BackElement, pair.Value.Back),
                WritePlaces(ForwardElement, pair.Value.Forward),
                WritePlaces(EditedElement, pair.Value.Edited))));
        XmlConfig.Save(_path, new XDocument(root));
    }

    private static List<PlaceEntry> ReadPlaces(XElement? list)
    {
        List<PlaceEntry> places = new();
        foreach (XElement place in list?.Elements(PlaceElement) ?? Enumerable.Empty<XElement>())
        {
            if (int.TryParse(XmlConfig.Attribute(place, "offset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
                && int.TryParse(XmlConfig.Attribute(place, "line"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int line)
                && DateTimeOffset.TryParse(XmlConfig.Attribute(place, "time"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset time))
            {
                places.Add(new PlaceEntry { BookPath = XmlConfig.Attribute(place, "book_path"), Offset = offset, Line = line, Time = time });
            }
        }

        return places;
    }

    private static XElement WritePlaces(string name, List<PlaceEntry> places) =>
        new(name, places.Select(p => new XElement(
            PlaceElement,
            new XAttribute("book_path", p.BookPath),
            new XAttribute("offset", p.Offset.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("line", p.Line.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("time", p.Time.ToString("O", CultureInfo.InvariantCulture)))));

    private static List<NavigationPlace> ToPlaces(List<PlaceEntry> entries) =>
        entries.Where(e => !string.IsNullOrEmpty(e.BookPath))
            .Select(e => new NavigationPlace(e.BookPath, e.Offset, e.Line, e.Time))
            .ToList();

    private static List<PlaceEntry> ToEntries(IEnumerable<NavigationPlace> places) =>
        places.Select(p => new PlaceEntry { BookPath = p.BookPath, Offset = p.Offset, Line = p.Line, Time = p.Time }).ToList();

    private sealed class BookEntry
    {
        public List<PlaceEntry> Back { get; set; } = new();

        public List<PlaceEntry> Forward { get; set; } = new();

        public List<PlaceEntry> Edited { get; set; } = new();
    }

    private sealed class PlaceEntry
    {
        public string BookPath { get; set; } = string.Empty;

        public int Offset { get; set; }

        public int Line { get; set; }

        public DateTimeOffset Time { get; set; }
    }
}
