namespace Signet.Core.Localization;

/// <summary>
/// Translations of the reference catalog entries (landmarks, guide, metadata fields, MARC and
/// ARIA roles, languages, special characters, Unicode blocks).
/// The resource key is built from the catalog prefix and the entry's <em>code</em> (e.g.
/// <c>Landmark_afterword_Name</c>), and the English text from the data is the fallback value — the catalogs
/// keep it as the source (and it is what ends up in the book content, e.g. nav titles).
/// </summary>
public static class CatalogText
{
    /// <summary>The translated entry name, or <paramref name="english"/> when there is no translation.</summary>
    public static string Name(string catalog, string code, string english) =>
        Lookup(catalog, code, "Name", english);

    /// <summary>The translated entry description, or <paramref name="english"/> when there is no translation.</summary>
    public static string Description(string catalog, string code, string english) =>
        Lookup(catalog, code, "Desc", english);

    /// <summary>The resource key for a catalog entry field (for the generator and tests).</summary>
    public static string Key(string catalog, string code, string field) => catalog + "_" + code + "_" + field;

    private static string Lookup(string catalog, string code, string field, string english) =>
        string.IsNullOrEmpty(english) ? english : CoreStrings.TryGet(Key(catalog, code, field)) ?? english;
}
