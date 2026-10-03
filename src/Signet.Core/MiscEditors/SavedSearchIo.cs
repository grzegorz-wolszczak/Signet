using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Signet.Core.MiscEditors;

/// <summary>The import/export file format of saved searches.</summary>
public enum SavedSearchFormat
{
    /// <summary>Native JSON.</summary>
    Json = 0,

    /// <summary>Tab-separated text (<c>.txt</c>).</summary>
    Tsv = 1,

    /// <summary>Comma-separated values (<c>.csv</c>).</summary>
    Csv = 2,
}

/// <summary>
/// Serialization / deserialization of the saved searches library.
/// The native format is JSON; TSV and CSV are supported additionally.
/// </summary>
public static class SavedSearchIo
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Deserializes the content of a native JSON file into a list of entries (tolerantly — an error = an empty list).</summary>
    public static IReadOnlyList<SearchEntry> ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SearchEntry>();
        }

        try
        {
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("search_entries", out JsonElement array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SearchEntry>();
            }

            var entries = new List<SearchEntry>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                string name = SearchEditorModel.NormalizeFullName(GetString(item, "Name"));
                entries.Add(SearchEntry.FromFullName(
                    name,
                    GetString(item, "Find"),
                    GetString(item, "Replace"),
                    GetString(item, "Controls")));
            }

            return entries;
        }
        catch (JsonException)
        {
            return Array.Empty<SearchEntry>();
        }
    }

    /// <summary>Serializes entries to native JSON (key <c>search_entries</c>, the <c>Name</c> field = the full name).</summary>
    public static string WriteJson(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var payload = new Dictionary<string, object>
        {
            ["search_entries"] = entries.Select(e => e.IsGroup
                ? new Dictionary<string, string> { ["Name"] = e.FullName }
                : new Dictionary<string, string>
                {
                    ["Name"] = e.FullName,
                    ["Find"] = e.Find,
                    ["Replace"] = e.Replace,
                    ["Controls"] = e.Controls,
                }).ToList(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Parses delimited text (TSV/CSV) into entries. An empty name field
    /// yields an automatic <c>&lt;groupName&gt;/repNNNNN</c>.
    /// </summary>
    public static IReadOnlyList<SearchEntry> ReadDelimited(string? text, char separator, string groupName)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<SearchEntry>();
        }

        string group = groupName.EndsWith('/') ? groupName : groupName + "/";
        var entries = new List<SearchEntry>();
        int count = 1;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            List<string> fields = separator == ','
                ? ParseCsvLine(line)
                : line.Split(separator).ToList();
            while (fields.Count < 4)
            {
                fields.Add(string.Empty);
            }

            string localName = "rep" + count.ToString("D5", CultureInfo.InvariantCulture);
            string fullName = fields[0].Length == 0 ? group + localName : fields[0];
            fullName = SearchEditorModel.NormalizeFullName(fullName);

            entries.Add(SearchEntry.FromFullName(fullName, fields[1], fields[2], fields[3]));
            count++;
        }

        return entries;
    }

    /// <summary>Serializes entries to delimited text.</summary>
    public static string WriteDelimited(IReadOnlyList<SearchEntry> entries, char separator)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var lines = new List<string>();
        foreach (SearchEntry entry in entries)
        {
            var fields = new List<string> { entry.FullName };
            if (!entry.IsGroup)
            {
                fields.Add(entry.Find);
                fields.Add(entry.Replace);
                fields.Add(entry.Controls);
            }

            lines.Add(separator == ','
                ? CreateCsvLine(fields)
                : string.Join(separator, fields));
        }

        return string.Join('\n', lines);
    }

    /// <summary>Splits one CSV line into fields (honoring quotes).</summary>
    public static List<string> ParseCsvLine(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var values = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        int n = data.Length;

        for (int i = 0; i < n; i++)
        {
            char c = data[i];
            if (!inQuote)
            {
                if (c == ',')
                {
                    values.Add(Unquote(sb.ToString().Trim()));
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                    if (c == '"')
                    {
                        inQuote = true;
                    }
                }
            }
            else
            {
                sb.Append(c);
                if (c == '"')
                {
                    if (i + 1 < n && data[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
            }
        }

        if (sb.Length > 0)
        {
            values.Add(Unquote(sb.ToString().Trim()));
        }

        return values;

        static string Unquote(string v)
        {
            if (v.StartsWith('"'))
            {
                v = v[1..];
            }

            if (v.EndsWith('"'))
            {
                v = v[..^1];
            }

            return v;
        }
    }

    /// <summary>Builds one CSV line from fields (quoting where needed).</summary>
    public static string CreateCsvLine(IReadOnlyList<string> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var cells = new List<string>();
        foreach (string val in data)
        {
            bool needQuotes = val.Contains(',', StringComparison.Ordinal);
            var sb = new StringBuilder();
            if (needQuotes)
            {
                sb.Append('"');
            }

            foreach (char c in val)
            {
                if (c == '"')
                {
                    sb.Append('"');
                }

                sb.Append(c);
            }

            if (needQuotes)
            {
                sb.Append('"');
            }

            cells.Add(sb.ToString());
        }

        return string.Join(',', cells);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
