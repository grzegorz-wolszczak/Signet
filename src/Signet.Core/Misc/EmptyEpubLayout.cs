using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Signet.Core.Misc;

/// <summary>
/// Reading and writing the empty EPUB layout from the "Custom Epub Layout Designer" wizard
/// (stored as JSON under the <c>bookpaths/empty_epub_bookpaths</c> key).
/// </summary>
public static class EmptyEpubLayout
{
    /// <summary>The default layout file name in the preferences folder.</summary>
    public const string DefaultLayoutFileName = "signet_empty_epub.json";

    private const string GroupKey = "bookpaths";
    private const string ListKey = "empty_epub_bookpaths";

    /// <summary>The full path of the default layout file in the preferences folder.</summary>
    public static string DefaultLayoutFilePath => Path.Combine(AppDirectories.PrefsDirectory, DefaultLayoutFileName);

    /// <summary>Reads the bookpaths from a layout file; an empty list when the file does not exist or is invalid.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            JsonArray? array = root?[GroupKey]?[ListKey] as JsonArray;
            if (array is null)
            {
                return Array.Empty<string>();
            }

            return array
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Reads the default layout from the preferences folder (an empty list when there is none).</summary>
    public static IReadOnlyList<string> ReadDefault() => Read(DefaultLayoutFilePath);

    /// <summary>Writes the bookpaths to a layout file (creating the folder if needed).</summary>
    public static void Write(string path, IReadOnlyList<string> bookPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bookPaths);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        JsonObject root = new()
        {
            [GroupKey] = new JsonObject
            {
                [ListKey] = new JsonArray(bookPaths.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            },
        };

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Writes the given layout as the default one in the preferences folder.</summary>
    public static void WriteDefault(IReadOnlyList<string> bookPaths)
    {
        AppDirectories.EnsurePrefsDirectory();
        Write(DefaultLayoutFilePath, bookPaths);
    }
}
