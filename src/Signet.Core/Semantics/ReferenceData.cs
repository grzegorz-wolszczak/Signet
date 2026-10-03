using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// A shared loader of the built-in reference tables (<c>EmbeddedData/*.tsv</c> /
/// <c>*.tsv.gz</c>) used by <see cref="MarcRelators"/>, <see cref="Language"/>,
/// <see cref="XmlEntities"/>, <see cref="AriaRoles"/>, <see cref="AriaClips"/>
/// and <see cref="CodepointNames"/>.
/// </summary>
internal static class ReferenceData
{
    /// <summary>
    /// Loads the rows of a TSV file (one row = one record, fields separated by <c>\t</c>).
    /// Empty lines and lines starting with <c>#</c> are skipped. Returns the rows
    /// in file order; every row has at least <paramref name="fieldCount"/> fields.
    /// </summary>
    public static IReadOnlyList<string[]> LoadTsv(string logicalName, int fieldCount)
    {
        List<string[]> rows = new();
        using Stream stream = OpenResource(logicalName);
        using StreamReader reader = new(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length < fieldCount)
            {
                throw new InvalidDataException(
                    $"Uszkodzony wiersz w zasobie {logicalName}: oczekiwano {fieldCount} pol, jest {fields.Length}.");
            }

            rows.Add(fields);
        }

        return rows;
    }

    /// <summary>Like <see cref="LoadTsv"/>, but for a gzip-compressed resource.</summary>
    public static IEnumerable<string[]> LoadTsvGz(string logicalName, int fieldCount)
    {
        using Stream raw = OpenResource(logicalName);
        using GZipStream gz = new(raw, CompressionMode.Decompress);
        using StreamReader reader = new(gz, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length < fieldCount)
            {
                throw new InvalidDataException(
                    $"Uszkodzony wiersz w zasobie {logicalName}: oczekiwano {fieldCount} pol, jest {fields.Length}.");
            }

            yield return fields;
        }
    }

    /// <summary>Loads the whole content of a text resource (UTF-8).</summary>
    public static string LoadText(string logicalName)
    {
        using Stream stream = OpenResource(logicalName);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Stream OpenResource(string logicalName)
    {
        return typeof(ReferenceData).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(CoreStrings.Format("Error_EmbeddedResourceMissing", logicalName));
    }
}
