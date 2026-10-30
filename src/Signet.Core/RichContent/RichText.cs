using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.RichContent;

/// <summary>How a fragment of a paragraph is set.</summary>
public enum RichSpanKind
{
    /// <summary>Plain text.</summary>
    Plain,

    /// <summary>Bold text.</summary>
    Bold,

    /// <summary>Italic text.</summary>
    Italic,

    /// <summary>Code: a fixed-width font and the color of code (a CSS keyword, a property, a file name…).</summary>
    Code,
}

/// <summary>A fragment of a paragraph. A <c>\n</c> in <see cref="Text"/> is a line break.</summary>
/// <param name="Text">The text.</param>
/// <param name="Kind">How it is set.</param>
public sealed record RichSpan(string Text, RichSpanKind Kind = RichSpanKind.Plain);

/// <summary>A block of a <see cref="RichText"/>.</summary>
public abstract record RichBlock;

/// <summary>A paragraph: fragments one after another, wrapped as a whole.</summary>
/// <param name="Spans">The fragments.</param>
public sealed record RichParagraph(IReadOnlyList<RichSpan> Spans) : RichBlock
{
    /// <inheritdoc />
    public bool Equals(RichParagraph? other) => other is not null && Spans.SequenceEqual(other.Spans);

    /// <inheritdoc />
    public override int GetHashCode() => Spans.Aggregate(0, (hash, span) => HashCode.Combine(hash, span));
}

/// <summary>A block of code in a fixed-width font, highlighted as <see cref="Language"/>.</summary>
/// <param name="Code">The code (lines separated by <c>\n</c>).</param>
/// <param name="Language">The syntax: <c>css</c>, <c>html</c>, <c>xml</c>, … or empty for none.</param>
public sealed record RichCodeBlock(string Code, string Language) : RichBlock;

/// <summary>A horizontal line between two blocks.</summary>
public sealed record RichSeparator : RichBlock;

/// <summary>
/// Formatted text for the places that need more than a plain string — a tooltip with a word in bold, values in the font
/// and color of code, a highlighted piece of CSS. Made with <see cref="RichTextBuilder"/> in code or read from the light
/// markup of <see cref="RichTextMarkup"/> (texts of the resources).
/// </summary>
public sealed class RichText : IEquatable<RichText>
{
    /// <summary>Creates a document from its blocks.</summary>
    public RichText(IReadOnlyList<RichBlock> blocks) => Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));

    /// <summary>The blocks, top to bottom.</summary>
    public IReadOnlyList<RichBlock> Blocks { get; }

    /// <summary>A document of one plain paragraph.</summary>
    public static RichText Plain(string text) => new(new RichBlock[] { new RichParagraph(new[] { new RichSpan(text ?? string.Empty) }) });

    /// <summary>
    /// The text without its formatting: paragraphs and code blocks separated by a blank line, a separator as
    /// <c>---</c> (for search, logs and tests).
    /// </summary>
    public string ToPlainText()
    {
        StringBuilder text = new();
        foreach (RichBlock block in Blocks)
        {
            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            text.Append(block switch
            {
                RichParagraph paragraph => string.Concat(paragraph.Spans.Select(s => s.Text)),
                RichCodeBlock code => code.Code,
                _ => "---",
            });
        }

        return text.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToPlainText();

    /// <inheritdoc />
    public bool Equals(RichText? other) => other is not null && Blocks.SequenceEqual(other.Blocks);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RichText);

    /// <inheritdoc />
    public override int GetHashCode() => Blocks.Aggregate(0, (hash, block) => HashCode.Combine(hash, block));
}

/// <summary>Builds a <see cref="RichText"/> block by block.</summary>
public sealed class RichTextBuilder
{
    private readonly List<RichBlock> _blocks = new();
    private List<RichSpan>? _paragraph;

    /// <summary>Plain text in the current paragraph.</summary>
    public RichTextBuilder Text(string text) => Add(text, RichSpanKind.Plain);

    /// <summary>Bold text in the current paragraph.</summary>
    public RichTextBuilder Bold(string text) => Add(text, RichSpanKind.Bold);

    /// <summary>Italic text in the current paragraph.</summary>
    public RichTextBuilder Italic(string text) => Add(text, RichSpanKind.Italic);

    /// <summary>Code in the current paragraph.</summary>
    public RichTextBuilder Code(string text) => Add(text, RichSpanKind.Code);

    /// <summary>A line break in the current paragraph.</summary>
    public RichTextBuilder LineBreak() => Add("\n", RichSpanKind.Plain);

    /// <summary>Ends the current paragraph; the next text starts a new one.</summary>
    public RichTextBuilder Paragraph()
    {
        if (_paragraph is { Count: > 0 })
        {
            _blocks.Add(new RichParagraph(_paragraph));
        }

        _paragraph = null;
        return this;
    }

    /// <summary>A horizontal line.</summary>
    public RichTextBuilder Separator()
    {
        Paragraph();
        _blocks.Add(new RichSeparator());
        return this;
    }

    /// <summary>A block of highlighted code.</summary>
    public RichTextBuilder CodeBlock(string code, string language)
    {
        Paragraph();
        _blocks.Add(new RichCodeBlock(code ?? string.Empty, language ?? string.Empty));
        return this;
    }

    /// <summary>The document.</summary>
    public RichText Build()
    {
        Paragraph();
        return new RichText(_blocks.ToArray());
    }

    private RichTextBuilder Add(string text, RichSpanKind kind)
    {
        if (string.IsNullOrEmpty(text))
        {
            return this;
        }

        _paragraph ??= new List<RichSpan>();

        // Two fragments of the same kind in a row are one (the markup and the builder then give the same document).
        if (_paragraph.Count > 0 && _paragraph[^1].Kind == kind)
        {
            _paragraph[^1] = _paragraph[^1] with { Text = _paragraph[^1].Text + text };
        }
        else
        {
            _paragraph.Add(new RichSpan(text, kind));
        }

        return this;
    }
}
