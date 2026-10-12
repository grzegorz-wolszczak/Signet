using System;

namespace Signet.Core.Themes;

/// <summary>What a <see cref="ThemeKey"/> holds.</summary>
public enum ThemeKeyKind
{
    /// <summary>A single color (e.g. the current line of the editor).</summary>
    Color,

    /// <summary>The look of a fragment of text (<see cref="TextAttributes"/>).</summary>
    TextAttributes,
}

/// <summary>The page of the theme editor a key is shown on (Preferences › Appearance › Color Scheme › …).</summary>
public enum ThemePage
{
    /// <summary>The editor in general: text, current line, selection, search, errors, hints.</summary>
    General,

    /// <summary>The XHTML syntax.</summary>
    Xhtml,

    /// <summary>The CSS syntax.</summary>
    Css,

    /// <summary>The JavaScript and JSON syntax.</summary>
    JavaScriptJson,

    /// <summary>The comparison of checkpoints.</summary>
    Diff,

    /// <summary>The Preview panel.</summary>
    Preview,

    /// <summary>The interface of the application.</summary>
    Interface,
}

/// <summary>
/// An element of a theme, e.g. <c>xhtml.tag</c> (IntelliJ's <c>TextAttributesKey</c> / <c>ColorKey</c> with its
/// <c>AttributesDescriptor</c>). The keys are fixed in the code (<see cref="ThemeKeys"/>); a theme gives them values.
/// </summary>
public sealed record ThemeKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="id">The identifier: English words joined with dots, e.g. <c>xhtml.attribute.name</c>.</param>
    /// <param name="page">The page of the theme editor.</param>
    /// <param name="group">The group in the tree of the page (an English identifier, e.g. <c>Errors</c>); empty = none.</param>
    /// <param name="kind">What the key holds.</param>
    /// <param name="baseId">The key whose values this one can inherit ("Inherit values from"); <c>null</c> = none.</param>
    /// <param name="defaultColor">For a color key: the value when no theme sets it; <c>null</c> = the platform's
    /// (e.g. the system accent).</param>
    /// <param name="defaultAttributes">For a text attributes key: the value when no theme sets it.</param>
    internal ThemeKey(
        string id,
        ThemePage page,
        string group,
        ThemeKeyKind kind,
        string? baseId,
        ThemeColor? defaultColor,
        TextAttributes? defaultAttributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Page = page;
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Kind = kind;
        BaseId = baseId;
        DefaultColor = defaultColor;
        DefaultAttributes = kind == ThemeKeyKind.TextAttributes ? defaultAttributes ?? TextAttributes.Empty : null;
    }

    /// <summary>The identifier, e.g. <c>xhtml.attribute.name</c>.</summary>
    public string Id { get; }

    /// <summary>The page of the theme editor.</summary>
    public ThemePage Page { get; }

    /// <summary>The group in the tree of the page; empty = none.</summary>
    public string Group { get; }

    /// <summary>What the key holds.</summary>
    public ThemeKeyKind Kind { get; }

    /// <summary>The key whose values this one can inherit; <c>null</c> = none.</summary>
    public string? BaseId { get; }

    /// <summary>For a color key: the value when no theme sets it; <c>null</c> = the platform's.</summary>
    public ThemeColor? DefaultColor { get; }

    /// <summary>For a text attributes key: the value when no theme sets it; <c>null</c> for a color key.</summary>
    public TextAttributes? DefaultAttributes { get; }
}
