using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>A feature a check found in a document, with where (upstream's <c>FeatureReport.Feature</c>).</summary>
public sealed record Feature(FeatureKind Name, EpubLocation Location, string? Value);

/// <summary>
/// What the checkers of the documents of a publication found in them, for the checks that need to know once all the
/// documents are read (upstream's <c>FeatureReport</c>): page breaks, a navigation document's lists, scripts, dictionaries…
/// </summary>
public sealed class FeatureReport
{
    private readonly List<Feature> _features = [];
    private readonly Lock _lock = new();

    /// <summary>Reports a feature.</summary>
    public void Report(FeatureKind name, EpubLocation location, string? value = null)
    {
        Feature feature = new(name, location, string.IsNullOrEmpty(value) ? null : value);
        lock (_lock)
        {
            if (!_features.Contains(feature))
            {
                _features.Add(feature);
            }
        }
    }

    /// <summary>Whether a feature of this kind was reported.</summary>
    public bool HasFeature(FeatureKind kind) => GetFeature(kind).Count > 0;

    /// <summary>The features of this kind, in the order they were reported.</summary>
    public IReadOnlyList<Feature> GetFeature(FeatureKind kind)
    {
        lock (_lock)
        {
            return [.. _features.Where(feature => feature.Name == kind)];
        }
    }
}
