using System;
using System.Collections.Generic;

namespace Signet.Core.Misc;

/// <summary>
/// Kind of formatting applied to a line fragment by the Code View highlighters; the view layer
/// takes the colors from <see cref="CodeViewAppearance"/>.
/// </summary>
public enum SyntaxFormat
{
    /// <summary><c>&lt;!DOCTYPE …&gt;</c> declaration (<c>XhtmlDoctypeColor</c>).</summary>
    XhtmlDoctype,

    /// <summary>Tag name and its closing <c>&gt;</c> (<c>XhtmlHtmlColor</c>).</summary>
    XhtmlTagName,

    /// <summary>HTML comment (<c>XhtmlHtmlCommentColor</c>).</summary>
    XhtmlComment,

    /// <summary>Content of a <c>&lt;style&gt;</c> block (<c>XhtmlCssColor</c>).</summary>
    XhtmlCss,

    /// <summary>Comment inside a <c>&lt;style&gt;</c> block (<c>XhtmlCssCommentColor</c>).</summary>
    XhtmlCssComment,

    /// <summary>Attribute name (<c>XhtmlAttributeNameColor</c>).</summary>
    XhtmlAttributeName,

    /// <summary>Attribute value (<c>XhtmlAttributeValueColor</c>).</summary>
    XhtmlAttributeValue,

    /// <summary>Entity, e.g. <c>&amp;#160</c> (<c>XhtmlEntityColor</c>).</summary>
    XhtmlEntity,

    /// <summary>Special space (NBSP etc.) — dashed underline in <c>XhtmlEntityColor</c>.</summary>
    XhtmlSpecialSpace,

    /// <summary>CSS selector, including pseudo-classes (<c>CssSelectorColor</c>).</summary>
    CssSelector,

    /// <summary>CSS property name (<c>CssPropertyColor</c>).</summary>
    CssProperty,

    /// <summary>CSS property value (<c>CssValueColor</c>).</summary>
    CssValue,

    /// <summary>Quoted string in CSS (<c>CssQuoteColor</c>).</summary>
    CssQuote,

    /// <summary>Comment in a CSS stylesheet (<c>CssCommentColor</c>).</summary>
    CssComment,

    // ---- Extended highlighting ----

    /// <summary>CSS class / id / pseudo-class selector: <c>.a</c>, <c>#b</c>, <c>:hover</c> (<c>CssSpecialSelectorColor</c>).</summary>
    CssSpecialSelector,

    /// <summary><c>@media</c>/<c>@import</c>/… rule and <c>!important</c> (<c>CssAtRuleColor</c>).</summary>
    CssAtRule,

    /// <summary>CSS constant: number, dimension, <c>#hex</c> color, color name (<c>CssConstantColor</c>).</summary>
    CssConstant,

    /// <summary>Namespace prefix, e.g. <c>svg:</c>, <c>epub:</c> (<c>XhtmlNamespacePrefixColor</c>).</summary>
    XhtmlNamespacePrefix,

    /// <summary>Text inside <c>&lt;b&gt;</c>/<c>&lt;strong&gt;</c>/a heading/<c>&lt;title&gt;</c> — bold.</summary>
    XhtmlBoldText,

    /// <summary>Text inside <c>&lt;i&gt;</c>/<c>&lt;em&gt;</c> — italic.</summary>
    XhtmlItalicText,

    /// <summary>Text that is both bold and italic.</summary>
    XhtmlBoldItalicText,

    /// <summary>Link target (<c>href</c>, <c>src</c>, <c>url()</c>) that exists in the book (<c>LinkColor</c>).</summary>
    Link,

    /// <summary>Link target missing from the book — link color + error squiggle.</summary>
    BadLink,

    /// <summary>Syntax error — squiggle in <c>ErrorUnderlineColor</c>; the kind is in <see cref="SyntaxSpan.Issue"/>.</summary>
    SyntaxError,
}

/// <summary>
/// Kind of error / note attached to a fragment — the content of the mouse-hover tooltip.
/// </summary>
public enum SyntaxIssue
{
    /// <summary>No notes.</summary>
    None,

    /// <summary>Unescaped <c>&lt;</c> in text.</summary>
    UnescapedLessThan,

    /// <summary>Unescaped <c>&amp;</c> in text.</summary>
    UnescapedAmpersand,

    /// <summary>Unescaped <c>&gt;</c> in text.</summary>
    UnescapedGreaterThan,

    /// <summary><c>/</c> in the middle of a tag (allowed only before <c>&gt;</c>).</summary>
    MisplacedSlash,

    /// <summary>Unexpected character in a tag.</summary>
    UnknownCharacter,

    /// <summary>A closing tag contains something besides the name.</summary>
    BadClosingTag,

    /// <summary>No attribute value after <c>=</c>.</summary>
    MissingAttributeValue,

    /// <summary>The tag name ends with a colon.</summary>
    PrefixOnlyTagName,

    /// <summary>Unterminated string in CSS.</summary>
    UnterminatedString,

    /// <summary>Link to a file that exists in the book (Ctrl+click hint).</summary>
    Link,

    /// <summary>Link to a file that is missing from the book.</summary>
    BrokenLink,
}

/// <summary>
/// A line fragment to format (start, length, format).
/// Fragments overlap — a later one overrides an earlier one.
/// </summary>
/// <param name="Start">Start within the line (0-based).</param>
/// <param name="Length">Length in characters.</param>
/// <param name="Format">Kind of formatting.</param>
/// <param name="Issue">Kind of error / tooltip for the fragment (extended highlighting).</param>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxFormat Format, SyntaxIssue Issue = SyntaxIssue.None);

/// <summary>
/// Line-by-line highlighter with state carried between lines (the previous line's end state is
/// the input of the next line). The state must have value equality: when a line's end state has
/// not changed, the lines below do not need to be re-highlighted.
/// </summary>
/// <typeparam name="TState">Line state type (a number, or an object holding a tag stack).</typeparam>
public interface ILineSyntaxHighlighter<TState>
    where TState : IEquatable<TState>
{
    /// <summary>State before the first line of the document.</summary>
    TState InitialState { get; }

    /// <summary>
    /// Highlights a single line (without the line terminator). Appends fragments to
    /// <paramref name="spans"/> (when not <c>null</c>) and returns the line's end state.
    /// </summary>
    TState HighlightLine(string text, TState previousState, List<SyntaxSpan>? spans);
}

/// <summary>Highlighter whose line state is an integer.</summary>
public interface ILineSyntaxHighlighter : ILineSyntaxHighlighter<int>
{
}
