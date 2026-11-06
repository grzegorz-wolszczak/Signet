using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// The metadata of a package document or a collection (upstream's <c>MetadataSet</c>): every <c>dc:*</c> and <c>meta</c>
/// element with its property, value and refinements. The primary ones are those that refine nothing. Refinements are
/// resolved when the set is built; a refinement cycle makes the build fail.
/// </summary>
public sealed class MetadataSet
{
    private const string FragmentPrefix = "#";

    private readonly List<Metadata> _all;
    private readonly List<Metadata> _primary;
    private readonly Dictionary<string, List<Metadata>> _refiners;
    private readonly List<KeyValuePair<Metadata, Metadata>> _refines;

    private MetadataSet(List<Metadata> all, List<Metadata> primary, List<KeyValuePair<Metadata, Metadata>> refines, Dictionary<string, List<Metadata>> refiners)
    {
        _all = all;
        _primary = primary;
        _refines = refines;
        _refiners = refiners;
    }

    /// <summary>The metadata that refine nothing, in the order they are in.</summary>
    public IReadOnlyList<Metadata> Primary => _primary;

    /// <summary>All the metadata.</summary>
    public IReadOnlyList<Metadata> All => _all;

    /// <summary>A builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Whether a primary metadatum has the property.</summary>
    public bool ContainsPrimary(VocabProperty property) => _primary.Exists(m => property.Equals(m.Property));

    /// <summary>Whether a primary metadatum has the property and the value.</summary>
    public bool ContainsPrimary(VocabProperty property, string value) => _primary.Exists(m => property.Equals(m.Property) && m.Value == value);

    /// <summary>Whether any metadatum has the property.</summary>
    public bool ContainsAny(VocabProperty property) => _all.Exists(m => property.Equals(m.Property));

    /// <summary>The primary metadata with the property.</summary>
    public IReadOnlyList<Metadata> GetPrimary(VocabProperty property) => [.. _primary.Where(m => property.Equals(m.Property))];

    /// <summary>All the metadata with the property.</summary>
    public IReadOnlyList<Metadata> GetAny(VocabProperty property) => [.. _all.Where(m => property.Equals(m.Property))];

    /// <summary>The metadatum that <paramref name="meta"/> refines, or <c>null</c>.</summary>
    public Metadata? GetRefinedBy(Metadata meta) => _refines.FirstOrDefault(pair => pair.Key.Equals(meta)).Value;

    /// <summary>The metadata that refine the element with the id <paramref name="id"/>.</summary>
    public IReadOnlyList<Metadata> GetRefining(string id) => _refiners.TryGetValue(id, out List<Metadata>? list) ? list : [];

    /// <summary>The first metadatum that has a refiner with the property (and the value when given).</summary>
    public static Metadata? TryFindInRefines(IEnumerable<Metadata> metas, VocabProperty property, string? value) =>
        metas.FirstOrDefault(meta => TryFind(meta.Refiners, property, value) is not null);

    /// <summary>The first metadatum with the property (and the value when given).</summary>
    public static Metadata? TryFind(IEnumerable<Metadata> metas, VocabProperty property, string? value) =>
        metas.FirstOrDefault(meta => property.Equals(meta.Property) && (value is null || value == meta.Value));

    /// <summary>A metadatum (upstream's <c>MetadataSet.Metadata</c>); equal when id, property, value and refined id are.</summary>
    public sealed class Metadata : IEquatable<Metadata>
    {
        internal Metadata(string? id, VocabProperty property, string? value, string? refines, IReadOnlyList<Metadata>? refiners)
        {
            Id = id;
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Value = value is null ? string.Empty : value.Trim();
            Refines = refines is null ? null
                : refines.StartsWith(FragmentPrefix, StringComparison.Ordinal) ? (refines.Length > 1 ? refines[1..] : null) : refines;
            Refiners = refiners ?? [];
        }

        /// <summary>The id, or <c>null</c>.</summary>
        public string? Id { get; }

        /// <summary>The property.</summary>
        public VocabProperty Property { get; }

        /// <summary>The value, trimmed.</summary>
        public string Value { get; }

        /// <summary>The id of the element this one refines, or <c>null</c>.</summary>
        public string? Refines { get; }

        /// <summary>The metadata that refine this one.</summary>
        public IReadOnlyList<Metadata> Refiners { get; }

        /// <summary>Whether the metadatum refines nothing.</summary>
        public bool IsPrimary => Refines is null;

        /// <inheritdoc />
        public override string ToString() => $"Metadata [id={Id}, property={Property}, value={Value}, refines={Refines}]";

        /// <inheritdoc />
        public bool Equals(Metadata? other) =>
            other is not null && Id == other.Id && Property.Equals(other.Property) && Refines == other.Refines && Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as Metadata);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Id, Property, Refines, Value);
    }

    /// <summary>Collects the metadata and resolves their refinements (upstream's <c>MetadataSet.Builder</c>).</summary>
    public sealed class Builder
    {
        private readonly List<Metadata> _primary = [];
        private readonly List<Metadata> _all = [];
        private readonly List<Metadata> _withId = [];
        private readonly Dictionary<string, List<Metadata>> _refiners = new(StringComparer.Ordinal);
        private readonly List<KeyValuePair<Metadata, Metadata>> _refines = [];
        private readonly Dictionary<Metadata, Visit> _visits = [];

        private enum Visit
        {
            Unvisited,
            Visited,
            Visiting,
        }

        /// <summary>Adds a metadatum.</summary>
        public Builder Meta(string? id, VocabProperty property, string? value, string? refines)
        {
            Metadata meta = new(id, property, value, refines, null);
            if (id is null)
            {
                if (meta.Refines is null)
                {
                    _primary.Add(meta);
                }

                _all.Add(meta);
            }
            else
            {
                _withId.Add(meta);
            }

            if (meta.Refines is not null)
            {
                RefinersOf(meta.Refines).Add(meta);
            }

            return this;
        }

        /// <summary>Resolves the refinements and builds the set.</summary>
        /// <exception cref="InvalidOperationException">The refinements are not a tree (<c>OPF-065</c>).</exception>
        public MetadataSet Build()
        {
            foreach (Metadata metadata in _withId.ToList())
            {
                BuildMeta(metadata);
            }

            return new MetadataSet(_all, _primary, _refines, _refiners);
        }

        private List<Metadata> RefinersOf(string id)
        {
            if (!_refiners.TryGetValue(id, out List<Metadata>? list))
            {
                _refiners[id] = list = [];
            }

            return list;
        }

        private Metadata BuildMeta(Metadata meta)
        {
            switch (_visits.GetValueOrDefault(meta))
            {
                case Visit.Visited:
                    return meta;
                case Visit.Visiting:
                    throw new InvalidOperationException("Not a DAG");
            }

            _visits[meta] = Visit.Visiting;
            List<Metadata> refiners = [];
            foreach (Metadata refiner in RefinersOf(meta.Id!).ToList())
            {
                refiners.Add(refiner.Id is not null ? BuildMeta(refiner) : refiner);
            }

            Metadata result = new(meta.Id, meta.Property, meta.Value, meta.Refines, refiners);
            foreach (Metadata refiner in refiners)
            {
                _refines.Add(new(refiner, result));
            }

            if (result.Refines is not null)
            {
                // The built metadatum replaces the one it was made from (and goes to the end of the refiners).
                List<Metadata> siblings = RefinersOf(result.Refines);
                int index = siblings.IndexOf(meta);
                if (index >= 0)
                {
                    siblings.RemoveAt(index);
                }

                siblings.Add(result);
            }
            else
            {
                _primary.Add(result);
            }

            _all.Add(result);
            _visits[meta] = Visit.Visited;
            return result;
        }
    }
}
