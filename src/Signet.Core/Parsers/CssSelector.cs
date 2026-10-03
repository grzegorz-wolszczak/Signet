using System.Collections.Generic;

namespace Signet.Core.Parsers;

/// <summary>
/// A single selector (after splitting a comma-separated group), with a detailed breakdown.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClassName"/> and <see cref="ElementName"/> are filled only when the selector contains
/// no combinator (space / <c>&gt;</c> / <c>~</c> / <c>+</c>) and no colon; <c>.a.b</c> gives <see cref="ClassName"/>
/// = <c>"a"</c> (only the first class), and <c>div.note</c> gives <c>div</c> + <c>note</c>. The
/// <c>GetClassSelectors</c> / <c>GetCssSelectorForElementClass</c> methods work on these fields.
/// </para>
/// <para>
/// The remaining fields (<see cref="ClassNames"/>, <see cref="Ids"/>, <see cref="ElementNames"/>,
/// <see cref="PseudoClasses"/>, <see cref="HasCombinator"/>, <see cref="HasPseudo"/>) are a
/// best-effort breakdown aware of <c>[]</c>, <c>()</c> and strings — used by
/// reports and "Delete Unused Styles".
/// </para>
/// </remarks>
public sealed class CssSelector
{
    internal CssSelector(
        int pos,
        string text,
        string elementName,
        string className,
        IReadOnlyList<string> elementNames,
        IReadOnlyList<string> classNames,
        IReadOnlyList<string> ids,
        IReadOnlyList<string> pseudoClasses,
        bool hasCombinator,
        bool hasPseudo)
    {
        Pos = pos;
        Text = text;
        ElementName = elementName;
        ClassName = className;
        ElementNames = elementNames;
        ClassNames = classNames;
        Ids = ids;
        PseudoClasses = pseudoClasses;
        HasCombinator = hasCombinator;
        HasPseudo = hasPseudo;
    }

    /// <summary>The 0-based offset of the start of the selector group in the source (including the <see cref="CssInfo"/> constructor offset).</summary>
    public int Pos { get; }

    /// <summary>The text of this selector (one member of the group, trimmed). The key when removing.</summary>
    public string Text { get; }

    /// <summary>The element name (see the class remarks) or <c>""</c>.</summary>
    public string ElementName { get; }

    /// <summary>The class name (see the class remarks) or <c>""</c>.</summary>
    public string ClassName { get; }

    /// <summary>All element names (type selectors) in order of occurrence.</summary>
    public IReadOnlyList<string> ElementNames { get; }

    /// <summary>All classes (<c>.foo</c>, without the dot) in order of occurrence.</summary>
    public IReadOnlyList<string> ClassNames { get; }

    /// <summary>All identifiers (<c>#bar</c>, without the hash) in order of occurrence.</summary>
    public IReadOnlyList<string> Ids { get; }

    /// <summary>The names of pseudo-classes / pseudo-elements (<c>:hover</c>, <c>::before</c>, <c>:nth-child(2)</c> → <c>nth-child</c>).</summary>
    public IReadOnlyList<string> PseudoClasses { get; }

    /// <summary>Whether the selector contains a combinator (space / <c>&gt;</c> / <c>~</c> / <c>+</c>) outside <c>[]</c> and <c>()</c>.</summary>
    public bool HasCombinator { get; }

    /// <summary>Whether the selector contains a colon (a pseudo-class or pseudo-element).</summary>
    public bool HasPseudo { get; }

    /// <summary>A comparer by <see cref="Pos"/> (for sorting when removing).</summary>
    public static IComparer<CssSelector> ByPosition { get; } =
        Comparer<CssSelector>.Create(static (a, b) => a.Pos.CompareTo(b.Pos));

    /// <inheritdoc/>
    public override string ToString() => $"{Text} @ {Pos}";
}

/// <summary>A CSS declaration (<c>property: value</c>) in a rule — the result of parsing a <see cref="CssRule"/>.</summary>
/// <param name="Property">The property name (trimmed).</param>
/// <param name="Value">The literal value, including a possible <c>!important</c> and comments.</param>
/// <param name="IsImportant">Whether the value ends with <c>!important</c>.</param>
public readonly record struct CssDeclaration(string Property, string Value, bool IsImportant);

/// <summary>
/// A CSS rule with source offsets — a view for removal and reports (<c>Delete Unused Styles</c>).
/// Covers selector rules as well as <c>@font-face</c> / <c>@page</c> blocks (then
/// <see cref="SelectorText"/> is empty and <see cref="AtRulePrelude"/> is filled in).
/// </summary>
public sealed class CssRule
{
    internal CssRule(
        string selectorText,
        IReadOnlyList<string> selectors,
        int selectorStart,
        int blockStart,
        int blockEnd,
        IReadOnlyList<CssDeclaration> declarations,
        string? atRulePrelude)
    {
        SelectorText = selectorText;
        Selectors = selectors;
        SelectorStart = selectorStart;
        BlockStart = blockStart;
        BlockEnd = blockEnd;
        Declarations = declarations;
        AtRulePrelude = atRulePrelude;
    }

    /// <summary>The full text of the selector group (<c>""</c> for <c>@font-face</c> blocks etc.).</summary>
    public string SelectorText { get; }

    /// <summary>The selector group split on commas (see <see cref="CssInfo.SplitGroupSelector"/>).</summary>
    public IReadOnlyList<string> Selectors { get; }

    /// <summary>The 0-based offset of the start of the selector group (or of the <c>@…</c> prelude) in the source.</summary>
    public int SelectorStart { get; }

    /// <summary>The 0-based offset of the opening <c>{</c>.</summary>
    public int BlockStart { get; }

    /// <summary>The 0-based offset right after the closing <c>}</c> (the end of the rule — for cutting the range).</summary>
    public int BlockEnd { get; }

    /// <summary>The declarations in the block (source order).</summary>
    public IReadOnlyList<CssDeclaration> Declarations { get; }

    /// <summary>The prelude of the enclosing <c>@…</c> rule (e.g. <c>@media screen</c>) or <c>null</c>.</summary>
    public string? AtRulePrelude { get; }
}
