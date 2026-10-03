using System.Collections.Generic;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The result of <see cref="Book.FindUnusedMediaResources"/>: when any (X)HTML file is not well-formed,
/// the detection is cancelled entirely.
/// </summary>
/// <param name="Applied">Whether the analysis was carried out.</param>
/// <param name="NotWellFormed">
/// The first file that failed the well-formed check (when <see cref="Applied"/> is
/// <c>false</c>), or <c>null</c>.
/// </param>
/// <param name="UnusedResources">
/// Media resources (image/SVG/video/audio) not referenced from any XHTML/CSS, excluding the
/// cover image.
/// </param>
public sealed record UnusedMediaResult(bool Applied, HtmlResource? NotWellFormed, IReadOnlyList<Resource> UnusedResources);

/// <summary>
/// The result of <see cref="Book.FindUnusedStyleSelectors"/>: the same well-formed guard as for
/// <see cref="UnusedMediaResult"/>.
/// </summary>
/// <param name="Applied">Whether the analysis was carried out.</param>
/// <param name="NotWellFormed">The first not-well-formed file (when <see cref="Applied"/> is <c>false</c>), or <c>null</c>.</param>
/// <param name="UnusedSelectors">
/// Selectors that are defined but never used, excluding the Media Overlays "active class" selectors
/// (<see cref="OpfResource.GetMediaOverlayActiveClassSelectors"/>).
/// </param>
public sealed record UnusedStyleSelectorsResult(bool Applied, HtmlResource? NotWellFormed, IReadOnlyList<CssSelectorUsage> UnusedSelectors);

/// <summary>
/// The result of <see cref="Book.FindCssCleanupCandidates"/> — the "Remove unused CSS rules + merge
/// identical selectors/properties" feature. The same well-formed guard as for <see cref="UnusedMediaResult"/>.
/// </summary>
/// <param name="Applied">Whether the analysis was carried out.</param>
/// <param name="NotWellFormed">The first not-well-formed file (when <see cref="Applied"/> is <c>false</c>), or <c>null</c>.</param>
/// <param name="MergeCandidates">
/// The groups of CSS rules eligible for merging (an identical selector or identical
/// properties) in each of the book's stylesheets.
/// </param>
/// <param name="UnusedStylesheets">The CSS stylesheets from the manifest that no XHTML file links.</param>
public sealed record CssCleanupResult(
    bool Applied,
    HtmlResource? NotWellFormed,
    IReadOnlyList<CssMergeCandidate> MergeCandidates,
    IReadOnlyList<CssResource> UnusedStylesheets);

/// <summary>The result of <see cref="Book.PrepareClassRename"/> — the same well-formed guard as for other whole-book operations.</summary>
/// <param name="Renamer">The ready <see cref="ClassRenamer"/> or <c>null</c> when the operation was held back.</param>
/// <param name="NotWellFormed">The first not-well-formed file or <c>null</c>.</param>
public sealed record ClassRenamePreparation(ClassRenamer? Renamer, HtmlResource? NotWellFormed);
