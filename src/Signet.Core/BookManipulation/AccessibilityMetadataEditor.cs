using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>The accessibility metadata of an EPUB 3 book as the Accessibility tool edits it.</summary>
public sealed class AccessibilityMetadataValues
{
    /// <summary><c>schema:accessibilitySummary</c>: a short description of the accessibility of the book; empty when there is none.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary><c>schema:accessMode</c>: how the content can be perceived (<c>textual</c>, <c>visual</c>…).</summary>
    public IReadOnlyList<string> AccessModes { get; set; } = [];

    /// <summary><c>schema:accessModeSufficient</c>: the modes that are enough to read the book (<c>textual</c>).</summary>
    public IReadOnlyList<string> AccessModesSufficient { get; set; } = [];

    /// <summary><c>schema:accessibilityFeature</c>: what the book offers (<c>alternativeText</c>, <c>tableOfContents</c>…).</summary>
    public IReadOnlyList<string> Features { get; set; } = [];

    /// <summary><c>schema:accessibilityHazard</c>: what can harm (<c>flashing</c>, <c>none</c>, <c>unknown</c>…).</summary>
    public IReadOnlyList<string> Hazards { get; set; } = [];

    /// <summary><c>dcterms:conformsTo</c>: the accessibility standard the book claims to meet.</summary>
    public IReadOnlyList<string> ConformsTo { get; set; } = [];
}

/// <summary>The values that the properties of the accessibility metadata can have (schema.org and the EPUB Accessibility specification).</summary>
public static class AccessibilityVocabulary
{
    /// <summary>The values of <c>schema:accessMode</c>.</summary>
    public static IReadOnlyList<string> AccessModes { get; } =
        ["auditory", "chartOnImage", "chemOnImage", "colorDependent", "diagramOnImage", "mathOnImage", "musicOnImage", "tactile", "textOnImage", "textual", "visual"];

    /// <summary>The values of <c>schema:accessModeSufficient</c> offered as single modes (the property takes a set; a value may be several joined by commas).</summary>
    public static IReadOnlyList<string> AccessModesSufficient { get; } = ["auditory", "tactile", "textual", "visual"];

    /// <summary>The values of <c>schema:accessibilityFeature</c>.</summary>
    public static IReadOnlyList<string> Features { get; } =
    [
        "alternativeText", "annotations", "ARIA", "audioDescription", "braille", "captions", "ChemML", "describedMath", "displayTransformability",
        "highContrastAudio", "highContrastDisplay", "index", "largePrint", "latex", "latex-chemistry", "longDescription", "MathML", "MathML-chemistry",
        "none", "pageBreakMarkers", "pageNavigation", "printPageNumbers", "readingOrder", "resizeText", "signLanguage", "structuralNavigation",
        "synchronizedAudioText", "tableOfContents", "taggedPDF", "tactileGraphic", "tactileObject", "timingControl", "transcript", "ttsMarkup",
        "unlocked",
    ];

    /// <summary>The values of <c>schema:accessibilityHazard</c>.</summary>
    public static IReadOnlyList<string> Hazards { get; } =
    [
        "flashing", "noFlashingHazard", "motionSimulation", "noMotionSimulationHazard", "sound", "noSoundHazard", "unknown", "none",
    ];

    /// <summary>The values of <c>dcterms:conformsTo</c> that the EPUB Accessibility 1.1 specification defines.</summary>
    public static IReadOnlyList<string> ConformsTo { get; } =
    [
        "EPUB Accessibility 1.1 - WCAG 2.0 Level A", "EPUB Accessibility 1.1 - WCAG 2.0 Level AA", "EPUB Accessibility 1.1 - WCAG 2.0 Level AAA",
        "EPUB Accessibility 1.1 - WCAG 2.1 Level A", "EPUB Accessibility 1.1 - WCAG 2.1 Level AA", "EPUB Accessibility 1.1 - WCAG 2.1 Level AAA",
        "EPUB Accessibility 1.1 - WCAG 2.2 Level A", "EPUB Accessibility 1.1 - WCAG 2.2 Level AA", "EPUB Accessibility 1.1 - WCAG 2.2 Level AAA",
    ];
}

/// <summary>Reads and writes the accessibility metadata of an EPUB 3 book (<c>meta property=…</c>, one value per element).</summary>
public static class AccessibilityMetadataEditor
{
    private const string SummaryProperty = "schema:accessibilitySummary";
    private const string AccessModeProperty = "schema:accessMode";
    private const string AccessModeSufficientProperty = "schema:accessModeSufficient";
    private const string FeatureProperty = "schema:accessibilityFeature";
    private const string HazardProperty = "schema:accessibilityHazard";
    private const string ConformsToProperty = "dcterms:conformsTo";

    /// <summary>The accessibility metadata of the book (refining metas do not count).</summary>
    public static AccessibilityMetadataValues Read(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        OpfResource opf = book.GetOpf();
        return new AccessibilityMetadataValues
        {
            Summary = opf.GetMetaPropertyValues(SummaryProperty).FirstOrDefault() ?? string.Empty,
            AccessModes = opf.GetMetaPropertyValues(AccessModeProperty),
            AccessModesSufficient = opf.GetMetaPropertyValues(AccessModeSufficientProperty),
            Features = opf.GetMetaPropertyValues(FeatureProperty),
            Hazards = opf.GetMetaPropertyValues(HazardProperty),
            ConformsTo = opf.GetMetaPropertyValues(ConformsToProperty),
        };
    }

    /// <summary>
    /// Makes <paramref name="values"/> the accessibility metadata: a property whose values differ is written anew (an empty list or summary
    /// removes it), a property that does not differ is not touched. Other metadata stays.
    /// </summary>
    public static void Write(Book book, AccessibilityMetadataValues values)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(values);
        OpfResource opf = book.GetOpf();
        bool changed = false;
        changed |= Replace(opf, SummaryProperty, string.IsNullOrWhiteSpace(values.Summary) ? [] : [values.Summary.Trim()]);
        changed |= Replace(opf, AccessModeProperty, values.AccessModes);
        changed |= Replace(opf, AccessModeSufficientProperty, values.AccessModesSufficient);
        changed |= Replace(opf, FeatureProperty, values.Features);
        changed |= Replace(opf, HazardProperty, values.Hazards);
        changed |= Replace(opf, ConformsToProperty, values.ConformsTo);
        if (changed)
        {
            book.Modified = true;
        }
    }

    private static bool Replace(OpfResource opf, string property, IReadOnlyList<string> values)
    {
        List<string> wanted = values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (opf.GetMetaPropertyValues(property).SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return false;
        }

        opf.RemoveMetaProperty(property);
        foreach (string value in wanted)
        {
            opf.AddMetaProperty(property, value);
        }

        return true;
    }
}
