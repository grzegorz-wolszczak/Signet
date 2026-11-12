using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Signet.Core.MainUI;

/// <summary>A line of a book file that carries a bookmark.</summary>
/// <param name="BookPath">Book path of the file.</param>
/// <param name="Line">The line (1-based).</param>
public sealed record BookmarkPlace(string BookPath, int Line)
{
    private const string BookPathKey = "p";
    private const string LineKey = "l";

    /// <summary>The places as the text kept in the settings (<see cref="Misc.SettingsStore.GetBookBookmarks"/>).</summary>
    public static string Serialize(IEnumerable<BookmarkPlace> places)
    {
        ArgumentNullException.ThrowIfNull(places);

        JsonArray array = new();
        foreach (BookmarkPlace place in places)
        {
            array.Add(new JsonObject { [BookPathKey] = place.BookPath, [LineKey] = place.Line });
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Reads <see cref="Serialize"/>d text. Text that is not valid yields no places; an entry that is not valid (no book
    /// path, a line below 1) is skipped, and so is a line that is already there (a line carries one bookmark).
    /// </summary>
    public static IReadOnlyList<BookmarkPlace> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<BookmarkPlace>();
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonArray array)
            {
                return Array.Empty<BookmarkPlace>();
            }

            List<BookmarkPlace> places = new();
            foreach (JsonNode? node in array)
            {
                if (node is JsonObject item
                    && item[BookPathKey]?.GetValue<string>() is { Length: > 0 } path
                    && item[LineKey]?.GetValue<int>() is >= 1 and var line)
                {
                    BookmarkPlace place = new(path, line);
                    if (!places.Contains(place))
                    {
                        places.Add(place);
                    }
                }
            }

            return places;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return Array.Empty<BookmarkPlace>();
        }
    }

    /// <summary>
    /// The place of the next (<paramref name="forward"/>) or previous bookmark in the file after
    /// <paramref name="currentLine"/>, wrapping around at the end of the file (like <c>GotoNextBookmarkInEditor</c> of the
    /// JetBrains IDEs); <see langword="null"/> when the file has no other bookmark than the one on that line.
    /// </summary>
    /// <param name="lines">The lines of the file that carry a bookmark, in any order.</param>
    /// <param name="currentLine">The line of the caret (1-based).</param>
    /// <param name="forward">Whether to look down the file (<see langword="false"/> = up).</param>
    public static int? Neighbour(IEnumerable<int> lines, int currentLine, bool forward)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<int> sorted = lines.Distinct().Order().ToList();
        if (forward)
        {
            foreach (int line in sorted)
            {
                if (line > currentLine)
                {
                    return line;
                }
            }

            return sorted.Count > 0 && sorted[0] != currentLine ? sorted[0] : null;
        }

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (sorted[i] < currentLine)
            {
                return sorted[i];
            }
        }

        return sorted.Count > 0 && sorted[^1] != currentLine ? sorted[^1] : null;
    }
}
