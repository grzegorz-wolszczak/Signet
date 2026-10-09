using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Signet.Core.MainUI;

/// <summary>
/// Keeps the <see cref="NavigationHistory"/> of saved books between sessions in one JSON file, per EPUB file path.
/// Places older than the given number of days are dropped on every load and save (a book left without places is
/// removed from the file), so the file does not grow with every book ever edited.
/// </summary>
public sealed class NavigationHistoryStore
{
    /// <summary>The longest time (days) a place is kept.</summary>
    public const int MaxDays = 14;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates a store on the given JSON file.</summary>
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
            ? new NavigationHistoryState(ToPlaces(entry.Back), ToPlaces(entry.Forward))
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
        books[Key(bookFilePath)] = new BookEntry { Back = ToEntries(state.Back), Forward = ToEntries(state.Forward) };
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
            };
            if (pruned.Back.Count > 0 || pruned.Forward.Count > 0)
            {
                kept[key] = pruned;
            }
        }

        return kept;
    }

    private Dictionary<string, BookEntry> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, BookEntry>(StringComparer.Ordinal);
            }

            FileContent? content = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(_path), JsonOptions);
            return new Dictionary<string, BookEntry>(content?.Books ?? new Dictionary<string, BookEntry>(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file only costs the saved history.
            return new Dictionary<string, BookEntry>(StringComparer.Ordinal);
        }
    }

    private void Write(Dictionary<string, BookEntry> books)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new FileContent { Books = books }, JsonOptions), Utf8NoBom);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static List<NavigationPlace> ToPlaces(List<PlaceEntry> entries) =>
        entries.Where(e => !string.IsNullOrEmpty(e.BookPath))
            .Select(e => new NavigationPlace(e.BookPath, e.Offset, e.Line, e.Time))
            .ToList();

    private static List<PlaceEntry> ToEntries(IEnumerable<NavigationPlace> places) =>
        places.Select(p => new PlaceEntry { BookPath = p.BookPath, Offset = p.Offset, Line = p.Line, Time = p.Time }).ToList();

    private sealed class FileContent
    {
        public Dictionary<string, BookEntry> Books { get; set; } = new();
    }

    private sealed class BookEntry
    {
        public List<PlaceEntry> Back { get; set; } = new();

        public List<PlaceEntry> Forward { get; set; } = new();
    }

    private sealed class PlaceEntry
    {
        public string BookPath { get; set; } = string.Empty;

        public int Offset { get; set; }

        public int Line { get; set; }

        public DateTimeOffset Time { get; set; }
    }
}
