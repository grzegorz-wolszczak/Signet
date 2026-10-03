namespace Signet.Core.Parsers;

/// <summary>
/// The kind of a CSS structure token.
/// </summary>
internal enum CssTokenType
{
    /// <summary><c>@charset</c> (a statement) — <c>Data</c> = the argument without the keyword and without <c>;</c>.</summary>
    CharsetAt = 0,

    /// <summary><c>@import</c> (a statement) — <c>Data</c> = the argument.</summary>
    ImportAt = 1,

    /// <summary><c>@namespace</c> (a statement) — <c>Data</c> = the argument.</summary>
    NamespaceAt = 2,

    /// <summary><c>@layer name;</c> (a statement) — <c>Data</c> = the argument.</summary>
    LayerAt = 3,

    /// <summary>The start of an <c>@…</c> rule with a block (<c>@media</c>, <c>@supports</c>, <c>@font-face</c>…) — <c>Data</c> = the full prelude with <c>@</c>.</summary>
    AtRuleBegin = 4,

    /// <summary>The <c>{</c> marker after the prelude of an <c>@…</c> rule.</summary>
    AtBlockBegin = 5,

    /// <summary>The <c>}</c> marker closing the block of an <c>@…</c> rule.</summary>
    AtBlockEnd = 6,

    /// <summary>An unknown <c>@…</c> rule in statement form — <c>Data</c> = the full text with <c>@</c>.</summary>
    AtRuleUnknown = 7,

    /// <summary>A selector group before <c>{</c> — <c>Data</c> = the group text (whitespace collapsed, trimmed).</summary>
    Selector = 8,

    /// <summary>A rule deemed structurally invalid (treated like a selector when serializing).</summary>
    InvalidRule = 9,

    /// <summary>The <c>{</c> marker after a selector group.</summary>
    SelBlockBegin = 10,

    /// <summary>The <c>}</c> marker closing the block of a selector rule.</summary>
    SelBlockEnd = 11,

    /// <summary>The property name in a declaration — <c>Data</c> = the name (trimmed).</summary>
    Property = 12,

    /// <summary>The property value — <c>Data</c> = the value without <c>;</c> (with a possible <c>!important</c>).</summary>
    PropertyValue = 13,

    /// <summary>A comment — <c>Data</c> = the full text including <c>/*</c> and <c>*/</c>.</summary>
    Comment = 14,

    /// <summary>The end-of-token-stream marker.</summary>
    CssEnd = 15,
}

/// <summary>
/// A single CSS structure token.
/// </summary>
/// <param name="Type">The token kind.</param>
/// <param name="Pos">The 0-based offset of the token's start in the source (after adding the <see cref="CssInfo"/> offset).</param>
/// <param name="Line">The 1-based line number of the token's start in the normalized source.</param>
/// <param name="Col">The 0-based column of the token's start.</param>
/// <param name="Data">The text payload, depending on <paramref name="Type"/>.</param>
internal readonly record struct CssToken(CssTokenType Type, int Pos, int Line, int Col, string Data);
