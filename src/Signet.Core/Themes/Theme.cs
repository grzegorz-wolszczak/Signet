using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Signet.Core.Themes;

/// <summary>The variant of the interface a theme uses (the light or the dark controls).</summary>
public enum InterfaceVariant
{
    /// <summary>Light controls.</summary>
    Light,

    /// <summary>Dark controls.</summary>
    Dark,
}

/// <summary>
/// The value a theme gives a key: its own color or attributes, or "inherit" — the values of the key's base in the same
/// theme (IntelliJ's "Inherit values from").
/// </summary>
public sealed record ThemeEntry
{
    /// <summary>"Inherit values from" the base of the key.</summary>
    public static ThemeEntry Inherited { get; } = new() { Inherit = true };

    /// <summary>Whether the key takes the values of its base.</summary>
    public bool Inherit { get; init; }

    /// <summary>For a color key: the color; <c>null</c> = the platform's (e.g. the system accent).</summary>
    public ThemeColor? Color { get; init; }

    /// <summary>For a text attributes key: the attributes.</summary>
    public TextAttributes? Attributes { get; init; }

    /// <summary>An entry with a color.</summary>
    public static ThemeEntry Of(ThemeColor? color) => new() { Color = color };

    /// <summary>An entry with attributes.</summary>
    public static ThemeEntry Of(TextAttributes attributes) =>
        new() { Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes)) };
}

/// <summary>
/// A theme of the interface or a color scheme of the editors (<see cref="ThemeDocumentKind"/>; the two have the same
/// shape and differ in the keys they hold). A theme of the user has a parent and keeps only what differs from it; a
/// bundled one has no parent and sets every key.
/// </summary>
public sealed record Theme
{
    /// <summary>Creates a theme.</summary>
    /// <param name="id">The identifier: a fixed one for the bundled themes, a ULID for the user's.</param>
    /// <param name="name">The name shown to the user.</param>
    public Theme(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    /// <summary>The identifier.</summary>
    public string Id { get; init; }

    /// <summary>The name shown to the user.</summary>
    public string Name { get; init; }

    /// <summary>The theme whose values this one takes where it sets none; <c>null</c> for a bundled theme.</summary>
    public string? ParentId { get; init; }

    /// <summary>The variant of the interface (a theme only); <c>null</c> = the parent's.</summary>
    public InterfaceVariant? Variant { get; init; }

    /// <summary>The values of the keys (by <see cref="ThemeKey.Id"/>) the theme sets itself.</summary>
    public ImmutableDictionary<string, ThemeEntry> Entries { get; init; } =
        ImmutableDictionary.Create<string, ThemeEntry>(StringComparer.Ordinal);

    /// <summary>When the theme was created (UTC); <c>null</c> for a bundled theme.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When the theme was last changed (UTC); <c>null</c> for a bundled theme.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>A copy with the value of a key set.</summary>
    public Theme With(string keyId, ThemeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return this with { Entries = Entries.SetItem(keyId, entry) };
    }

    /// <summary>A copy without the own value of a key (it takes the parent's again).</summary>
    public Theme Without(string keyId) => this with { Entries = Entries.Remove(keyId) };

    /// <summary>A copy with the values of several keys set.</summary>
    public Theme With(IEnumerable<KeyValuePair<string, ThemeEntry>> entries) =>
        this with { Entries = Entries.SetItems(entries) };
}
