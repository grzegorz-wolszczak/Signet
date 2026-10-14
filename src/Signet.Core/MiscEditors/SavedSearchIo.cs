using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>The import/export file format of saved searches.</summary>
public enum SavedSearchFormat
{
    /// <summary>Native XML (<c>.xml</c>).</summary>
    Xml = 0,

    /// <summary>Tab-separated text (<c>.txt</c>).</summary>
    Tsv = 1,

    /// <summary>Comma-separated values (<c>.csv</c>).</summary>
    Csv = 2,
}

/// <summary>
/// Serialization / deserialization of the saved searches library.
/// The native format is XML (the store file); TSV and CSV are supported additionally for import/export.
/// </summary>
/// <remarks>
/// XML — groups nest (<see cref="GroupedXml"/>), so names may contain <c>/</c>:
/// <code>
/// &lt;saved_searches&gt;
///   &lt;group name="Typography"&gt;
///     &lt;search name="Opening quote"&gt;
///       &lt;find&gt;"(\w)&lt;/find&gt;
///       &lt;replace&gt;„\1&lt;/replace&gt;
///       &lt;controls&gt;RX DN AH&lt;/controls&gt;
///     &lt;/search&gt;
///   &lt;/group&gt;
/// &lt;/saved_searches&gt;
/// </code>
/// TSV/CSV keep the group path in the first field joined with <c>/</c>, so there a <c>/</c> in a name reads back as a
/// group separator.
/// </remarks>
public static class SavedSearchIo
{
    private const string RootElement = "saved_searches";
    private const string SearchElement = "search";
    private const string FindElement = "find";
    private const string ReplaceElement = "replace";
    private const string ControlsElement = "controls";

    /// <summary>Reads the entries of a saved searches XML text (leniently — an error or another document yields an empty list).</summary>
    public static IReadOnlyList<SearchEntry> ReadXml(string? xml) => FromDocument(XmlConfig.TryParse(xml));

    /// <summary>Writes entries as a saved searches XML text.</summary>
    public static string WriteXml(IReadOnlyList<SearchEntry> entries) => XmlConfig.ToText(ToDocument(entries));

    /// <summary>The entries of a saved searches XML document (an empty list for <see langword="null"/> or another document).</summary>
    public static IReadOnlyList<SearchEntry> FromDocument(XDocument? document)
    {
        if (document?.Root is not { } root || root.Name != RootElement)
        {
            return Array.Empty<SearchEntry>();
        }

        return GroupedXml.Read(
            root,
            SearchElement,
            (path, element) => new SearchEntry(
                false,
                path,
                XmlConfig.Attribute(element, GroupedXml.NameAttribute),
                XmlConfig.ChildText(element, FindElement),
                XmlConfig.ChildText(element, ReplaceElement),
                XmlConfig.ChildText(element, ControlsElement)),
            (path, name) => new SearchEntry(true, path, name, string.Empty, string.Empty, string.Empty));
    }

    /// <summary>The saved searches XML document of the entries.</summary>
    public static XDocument ToDocument(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        XElement root = new(RootElement);
        GroupedXml.Write(
            root,
            entries,
            e => e.GroupPath,
            e => e.IsGroup,
            e => e.Name,
            e => new XElement(
                SearchElement,
                new XAttribute(GroupedXml.NameAttribute, e.Name),
                new XElement(FindElement, e.Find),
                new XElement(ReplaceElement, e.Replace),
                new XElement(ControlsElement, e.Controls)));
        return new XDocument(root);
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
}
