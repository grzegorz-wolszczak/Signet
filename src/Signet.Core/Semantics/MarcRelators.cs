using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// The dictionary of MARC relators (contributor roles, e.g. <c>aut</c> = Author) — code, name,
/// description. Data: <c>EmbeddedData/marc-relators.tsv</c>.
/// </summary>
/// <remarks>
/// Names and descriptions are localized through the catalog (as in <see cref="GuideItems"/> / <see cref="Landmarks"/>).
/// </remarks>
public static class MarcRelators
{
    private static readonly IReadOnlyList<string[]> Data =
        ReferenceData.LoadTsv("ReferenceData.marc-relators", 3);

    // On a key collision the last entry wins
    // (the data contains e.g. a duplicated code "rce").
    private static readonly Dictionary<string, DescriptiveInfo> CodeMap = BuildCodeMap();

    private static readonly Dictionary<string, string> NameMap = BuildNameMap();

    private static Dictionary<string, DescriptiveInfo> BuildCodeMap()
    {
        Dictionary<string, DescriptiveInfo> map = new(StringComparer.Ordinal);
        foreach (string[] row in Data)
        {
            map[row[0]] = new DescriptiveInfo(row[1], row[2]);
        }

        return map;
    }

    private static Dictionary<string, string> BuildNameMap()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (string[] row in Data)
        {
            map[row[1]] = row[0];
        }

        return map;
    }

    private const string Catalog = "Marc";

    /// <summary>The role name (in the UI language) for a code; empty if the code is unknown.</summary>
    public static string GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? CatalogText.Name(Catalog, code, info.Name) : string.Empty;
    }

    /// <summary>The role description (in the UI language) for a code; empty if the code is unknown.</summary>
    public static string GetDescriptionByCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? CatalogText.Description(Catalog, code, info.Description) : string.Empty;
    }

    /// <summary>The description for a role name (English or translated); empty if unknown.</summary>
    public static string GetDescriptionByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string code = GetCode(name);
        return code.Length > 0 ? GetDescriptionByCode(code) : string.Empty;
    }

    /// <summary>The code for a role name (English or translated); empty if unknown.</summary>
    public static string GetCode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (NameMap.TryGetValue(name, out string? code))
        {
            return code;
        }

        foreach (string candidate in CodeMap.Keys)
        {
            if (string.Equals(GetName(candidate), name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>The role names sorted, in the UI language.</summary>
    public static IReadOnlyList<string> GetSortedNames() =>
        CodeMap.Keys.Select(GetName).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>Whether the given string is a known role code.</summary>
    public static bool IsRelatorCode(string code) => code is not null && CodeMap.ContainsKey(code);

    /// <summary>Whether the given string is a known role name (English or translated).</summary>
    public static bool IsRelatorName(string name) => name is not null && GetCode(name).Length > 0;

    /// <summary>A code -&gt; role name and description map in the UI language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetCodeMap() =>
        CodeMap.Keys.ToDictionary(c => c, c => new DescriptiveInfo(GetName(c), GetDescriptionByCode(c)), StringComparer.Ordinal);
}
