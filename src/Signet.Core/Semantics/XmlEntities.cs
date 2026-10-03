using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Signet.Core.Semantics;

/// <summary>
/// Named XML/HTML entities (the supported subset) — code point, name
/// (e.g. <c>nbsp</c>), description. Data: <c>EmbeddedData/xml-entities.tsv</c>.
/// </summary>
/// <remarks>Descriptions are not localized.</remarks>
public static class XmlEntities
{
    private static readonly IReadOnlyList<string[]> Data =
        ReferenceData.LoadTsv("ReferenceData.xml-entities", 3);

    private static readonly Dictionary<ushort, string> EntityName =
        Data.ToDictionary(r => ushort.Parse(r[0], CultureInfo.InvariantCulture), r => r[1]);

    private static readonly Dictionary<ushort, string> EntityDescription =
        Data.ToDictionary(r => ushort.Parse(r[0], CultureInfo.InvariantCulture), r => r[2]);

    private static readonly Dictionary<string, ushort> EntityCodeMap =
        BuildCodeMap(Data);

    private static Dictionary<string, ushort> BuildCodeMap(IReadOnlyList<string[]> data)
    {
        Dictionary<string, ushort> map = new(StringComparer.Ordinal);
        foreach (string[] row in data)
        {
            map[row[1]] = ushort.Parse(row[0], CultureInfo.InvariantCulture);
        }

        return map;
    }

    /// <summary>The entity name for a code point; an empty string if none.</summary>
    public static string GetEntityName(ushort code) => EntityName.GetValueOrDefault(code, string.Empty);

    /// <summary>The entity description for a code point; an empty string if none.</summary>
    public static string GetEntityDescription(ushort code) => EntityDescription.GetValueOrDefault(code, string.Empty);

    /// <summary>
    /// The code point for an entity notation (<c>&amp;name;</c>, <c>&amp;#DEC;</c> or <c>&amp;#xHEX;</c>).
    /// Returns <c>0</c> if the string is not a valid entity or the name is unknown.
    /// </summary>
    public static ushort GetEntityCode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.StartsWith('&') || !name.EndsWith(';'))
        {
            return 0;
        }

        string root = name.Substring(1, name.Length - 2);
        if (root.StartsWith('#'))
        {
            root = root.Substring(1);
            NumberStyles style = NumberStyles.Integer;
            if (root.StartsWith('x') || root.StartsWith('X'))
            {
                style = NumberStyles.HexNumber;
                root = root.Substring(1);
            }

            return ushort.TryParse(root, style, CultureInfo.InvariantCulture, out ushort rcode) ? rcode : (ushort)0;
        }

        return EntityCodeMap.GetValueOrDefault(root, (ushort)0);
    }

    /// <summary>All entity code points (in the order of the source data).</summary>
    public static IReadOnlyList<ushort> GetAllCodes() =>
        Data.Select(r => ushort.Parse(r[0], CultureInfo.InvariantCulture)).ToList();
}
