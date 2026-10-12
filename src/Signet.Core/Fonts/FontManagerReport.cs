using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Fonts;

/// <summary>A font file of the book and the <c>@font-face</c> rules that use it.</summary>
/// <param name="Font">The font resource.</param>
/// <param name="Candidate">What the file says about itself and what to warn about.</param>
/// <param name="Rules">The rules whose <c>src</c> points to the file.</param>
/// <param name="Size">The file size in bytes (0 when unknown).</param>
public sealed record FontFileReport(FontResource Font, FontFaceCandidate Candidate, IReadOnlyList<FontFaceRule> Rules, long Size)
{
    /// <summary>Whether no <c>@font-face</c> rule uses the file.</summary>
    public bool IsUnused => Rules.Count == 0;
}

/// <summary>A font family of the book: embedded (it has <c>@font-face</c> rules) or only named somewhere.</summary>
/// <param name="Family">The family name (as first declared or named).</param>
/// <param name="Key">The family as compared (<see cref="FontFaceDescriptors.FamilyKey"/>).</param>
/// <param name="Rules">Its <c>@font-face</c> rules (empty for a family that is not embedded).</param>
/// <param name="Usages">The declarations that name it.</param>
/// <param name="Elements">How many elements with text are rendered in it (embedded families only).</param>
/// <param name="Missing">The faces it lacks.</param>
public sealed record FontFamilyReport(
    string Family,
    string Key,
    IReadOnlyList<FontFaceRule> Rules,
    IReadOnlyList<FontFamilyUsage> Usages,
    int Elements,
    IReadOnlyList<MissingFontVariant> Missing)
{
    /// <summary>Whether the family has <c>@font-face</c> rules.</summary>
    public bool IsEmbedded => Rules.Count > 0;

    /// <summary>Whether the family is embedded but nothing names it.</summary>
    public bool IsUnused => IsEmbedded && Usages.Count == 0;

    /// <summary>The faces of the family (e.g. <c>normal, italic, bold, bold italic</c>).</summary>
    public string FacesText => string.Join(
        ", ",
        Rules.OrderBy(r => r.Weight.Min).ThenBy(r => r.Style, StringComparer.Ordinal)
            .Select(r => FontFaceDescriptors.Describe(r.Weight, r.Style))
            .Distinct(StringComparer.Ordinal));
}

/// <summary>The font files and font families of a book, for "Manage Fonts".</summary>
public sealed class FontManagerReport
{
    private FontManagerReport(BookFontIndex index, IReadOnlyList<FontFileReport> files, IReadOnlyList<FontFamilyReport> families)
    {
        Index = index;
        Files = files;
        Families = families;
    }

    /// <summary>The index the report was built from.</summary>
    public BookFontIndex Index { get; }

    /// <summary>The font files, in book order.</summary>
    public IReadOnlyList<FontFileReport> Files { get; }

    /// <summary>The embedded families (alphabetically), then the families only named somewhere.</summary>
    public IReadOnlyList<FontFamilyReport> Families { get; }

    /// <summary>Builds the report for the current texts of the book.</summary>
    public static FontManagerReport Build(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        BookFontIndex index = BookFontIndex.Build(book);
        FontUsageReport usage = FontUsageAnalyzer.Analyze(book, index);

        List<FontFileReport> files = book.GetAllResources().OfType<FontResource>()
            .Select(font => new FontFileReport(font, FontFaceAddition.Describe(font, book.IsEpub3), index.RulesForFont(font.BookPath), SizeOf(font)))
            .ToList();

        List<FontFamilyReport> families = index.Rules
            .Where(r => r.Family.Length > 0)
            .GroupBy(r => r.FamilyKey)
            .Select(g => new FontFamilyReport(
                g.First().Family,
                g.Key,
                g.ToList(),
                index.UsagesOfFamily(g.First().Family),
                usage.ElementsByFamily.TryGetValue(g.Key, out int count) ? count : 0,
                usage.Missing.Where(m => string.Equals(FontFaceDescriptors.FamilyKey(m.Family), g.Key, StringComparison.Ordinal)).ToList()))
            .OrderBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> embedded = families.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        families.AddRange(index.Usages
            .SelectMany(u => u.Families)
            .Where(f => !f.IsGeneric && f.Name.Length > 0 && !embedded.Contains(FontFaceDescriptors.FamilyKey(f.Name)))
            .GroupBy(f => FontFaceDescriptors.FamilyKey(f.Name))
            .Select(g => new FontFamilyReport(
                g.First().Name,
                g.Key,
                Array.Empty<FontFaceRule>(),
                index.UsagesOfFamily(g.First().Name),
                0,
                Array.Empty<MissingFontVariant>()))
            .OrderBy(f => f.Family, StringComparer.OrdinalIgnoreCase));

        return new FontManagerReport(index, files, families);
    }

    private static long SizeOf(FontResource font)
    {
        try
        {
            return new FileInfo(font.FullPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
