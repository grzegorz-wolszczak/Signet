using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Signet.Core.MiscEditors;

/// <summary>
/// Serialization / deserialization of the clip library. The native format is JSON — the only
/// supported import/export format.
/// </summary>
public static class ClipIo
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Deserializes the content of a native JSON file into a list of entries (leniently — an error yields an empty list).</summary>
    public static IReadOnlyList<ClipEntry> ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ClipEntry>();
        }

        try
        {
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("clip_entries", out JsonElement array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ClipEntry>();
            }

            var entries = new List<ClipEntry>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                string name = ClipEditorModel.NormalizeFullName(GetString(item, "Name"));
                entries.Add(ClipEntry.FromFullName(name, GetString(item, "Text")));
            }

            return entries;
        }
        catch (JsonException)
        {
            return Array.Empty<ClipEntry>();
        }
    }

    /// <summary>Serializes entries to native JSON (key <c>clip_entries</c>, field <c>Name</c> = full name).</summary>
    public static string WriteJson(IReadOnlyList<ClipEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var payload = new Dictionary<string, object>
        {
            ["clip_entries"] = entries.Select(e => e.IsGroup
                ? new Dictionary<string, string> { ["Name"] = e.FullName }
                : new Dictionary<string, string> { ["Name"] = e.FullName, ["Text"] = e.Text }).ToList(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
