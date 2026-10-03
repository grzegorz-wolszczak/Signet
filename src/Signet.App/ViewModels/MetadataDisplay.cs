using System;
using System.Collections.Generic;
using Signet.Core.Metadata;
using Signet.Core.Semantics;

namespace Signet.App.ViewModels;

/// <summary>
/// Code &#8596; display name translation for the "Metadata Editor" (the helpers
/// <c>EName</c>/<c>ECode</c>/<c>PName</c>/<c>PCode</c>/<c>RName</c>/<c>RCode</c>/
/// <c>LName</c>/<c>LCode</c>). Used only to display labels
/// (the "Name" column) and selection lists in the "Add Metadata"/"Add Property" dialogs — values edited
/// inline stay raw codes.
/// </summary>
internal static class MetadataDisplay
{
    /// <summary>Element name for a code (e.g. <c>dc:creator</c> -&gt; "Creator").</summary>
    public static string EName(bool isEpub3, string code)
    {
        IReadOnlyDictionary<string, DescriptiveInfo> table = isEpub3 ? MetadataFieldCatalog.Epub3Elements : MetadataFieldCatalog.Epub2Elements;
        return table.TryGetValue(code, out DescriptiveInfo info) ? info.Name : code;
    }

    /// <summary>Element code for a name.</summary>
    public static string ECode(bool isEpub3, string name)
    {
        // Reverse maps are computed on every call: the names are in the UI language (CoreStrings).
        Dictionary<string, string> map = BuildReverse(isEpub3 ? MetadataFieldCatalog.Epub3Elements : MetadataFieldCatalog.Epub2Elements);
        return map.GetValueOrDefault(name, name);
    }

    /// <summary>Property/attribute name for a code (checks property first, then xproperty).</summary>
    public static string PName(bool isEpub3, string code)
    {
        IReadOnlyDictionary<string, DescriptiveInfo> properties = isEpub3 ? MetadataFieldCatalog.Epub3Properties : MetadataFieldCatalog.Epub2Properties;
        if (properties.TryGetValue(code, out DescriptiveInfo info))
        {
            return info.Name;
        }

        IReadOnlyDictionary<string, DescriptiveInfo> xproperties = isEpub3 ? MetadataFieldCatalog.Epub3XProperties : MetadataFieldCatalog.Epub2XProperties;
        return xproperties.TryGetValue(code, out DescriptiveInfo xinfo) ? xinfo.Name : code;
    }

    /// <summary>Property/attribute code for a name.</summary>
    public static string PCode(bool isEpub3, string name)
    {
        Dictionary<string, string> properties = BuildReverse(isEpub3 ? MetadataFieldCatalog.Epub3Properties : MetadataFieldCatalog.Epub2Properties);
        if (properties.TryGetValue(name, out string? code))
        {
            return code;
        }

        Dictionary<string, string> xproperties = BuildReverse(isEpub3 ? MetadataFieldCatalog.Epub3XProperties : MetadataFieldCatalog.Epub2XProperties);
        return xproperties.GetValueOrDefault(name, name);
    }

    /// <summary>MARC relator name for a code (e.g. <c>aut</c> -&gt; "Author").</summary>
    public static string RName(string code) =>
        code.Length > 0 && MarcRelators.GetName(code) is { Length: > 0 } name ? name : code;

    /// <summary>MARC relator code for a name.</summary>
    public static string RCode(string name) => MarcRelators.IsRelatorName(name) ? MarcRelators.GetCode(name) : name;

    /// <summary>English language name for a code.</summary>
    public static string LName(string code) => Language.GetLanguageName(code, code);

    /// <summary>Language code for a name.</summary>
    public static string LCode(string name) => Language.GetLanguageCode(name, name);

    private static Dictionary<string, string> BuildReverse(IReadOnlyDictionary<string, DescriptiveInfo> table)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DescriptiveInfo> pair in table)
        {
            map[pair.Value.Name] = pair.Key;
        }

        return map;
    }
}
