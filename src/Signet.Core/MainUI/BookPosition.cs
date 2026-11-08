using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Signet.Core.MainUI;

/// <summary>The place and size of a window a tab was dragged out into.</summary>
/// <param name="X">Left edge on the screen.</param>
/// <param name="Y">Top edge on the screen.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record FloatingPlace(double X, double Y, double Width, double Height);

/// <summary>Where one tab of a book was left: the caret and the first visible line (<c>TopLine</c> 0 = unknown).</summary>
/// <param name="BookPath">Book path of the tab's file.</param>
/// <param name="CaretOffset">Caret offset in the file; -1 when the tab has none (not a Code Editor tab).</param>
/// <param name="TopLine">First visible line (1-based), 0 when unknown.</param>
/// <param name="Floating">The window of its own the tab was in, or <see langword="null"/> when it was in the main window.</param>
public sealed record BookTabPosition(string BookPath, int CaretOffset, int TopLine, FloatingPlace? Floating = null);

/// <summary>
/// Where the user was in a book when it was closed: the open tabs (in order) with their positions and the active
/// one. Kept per book file next to the recent files list (<see cref="Misc.SettingsStore.GetBookPosition"/>) and
/// forgotten with its entry there.
/// </summary>
/// <param name="Tabs">The open tabs in display order.</param>
/// <param name="ActiveBookPath">Book path of the active tab, or <see langword="null"/>.</param>
public sealed record BookPosition(IReadOnlyList<BookTabPosition> Tabs, string? ActiveBookPath)
{
    /// <summary>The position as the text kept in the settings.</summary>
    public string Serialize()
    {
        JsonArray tabs = new();
        foreach (BookTabPosition tab in Tabs)
        {
            JsonObject item = new()
            {
                ["p"] = tab.BookPath,
                ["c"] = tab.CaretOffset,
                ["t"] = tab.TopLine,
            };
            if (tab.Floating is { } place)
            {
                item["f"] = new JsonArray(place.X, place.Y, place.Width, place.Height);
            }

            tabs.Add(item);
        }

        JsonObject root = new() { ["tabs"] = tabs };
        if (!string.IsNullOrEmpty(ActiveBookPath))
        {
            root["active"] = ActiveBookPath;
        }

        return root.ToJsonString();
    }

    /// <summary>Reads <see cref="Serialize"/>d text; <see langword="null"/> when it is not a valid position.</summary>
    public static BookPosition? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["tabs"] is not JsonArray array)
            {
                return null;
            }

            List<BookTabPosition> tabs = new();
            foreach (JsonNode? node in array)
            {
                if (node is JsonObject item && item["p"]?.GetValue<string>() is { Length: > 0 } path)
                {
                    FloatingPlace? floating = item["f"] is JsonArray { Count: 4 } f
                        && f[0] is { } x && f[1] is { } y && f[2] is { } w && f[3] is { } h
                        ? new FloatingPlace(x.GetValue<double>(), y.GetValue<double>(), w.GetValue<double>(), h.GetValue<double>())
                        : null;
                    tabs.Add(new BookTabPosition(
                        path,
                        item["c"]?.GetValue<int>() ?? -1,
                        item["t"]?.GetValue<int>() ?? 0,
                        floating));
                }
            }

            string? active = root["active"]?.GetValue<string>();
            return new BookPosition(tabs, string.IsNullOrEmpty(active) ? null : active);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
