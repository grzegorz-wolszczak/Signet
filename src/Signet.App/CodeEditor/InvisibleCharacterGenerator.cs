using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Shows the characters the editor would otherwise draw as nothing: the soft hyphen (U+00AD), the non-breaking space
/// (U+00A0) and the characters of zero width. Each is replaced by a mark of its own in the color of
/// <see cref="ThemeKeys.EditorInvisibleCharacters"/>; the character itself is untouched, only what is drawn in its
/// place changes.
/// </summary>
/// <remarks>
/// Found in November 2026: a book hyphenated with soft hyphens looked as if the editor broke words in two (a word
/// wrapped in the middle, with nothing to say why) and the spell checker seemed to check halves of words. What goes
/// in their place is settled by the three settings of Code Editor
/// (<see cref="SettingsStore.CodeEditorShowSoftHyphens"/> and its neighbours).
/// </remarks>
internal sealed class InvisibleCharacterGenerator : VisualLineElementGenerator
{
    /// <summary>The mark of a soft hyphen — a hyphen that cannot be taken for one typed by hand.</summary>
    public const string SoftHyphenMark = "¬";

    /// <summary>The mark of a non-breaking space (as word processors show it).</summary>
    public const string NonBreakingSpaceMark = "°";

    /// <summary>The mark of a character of zero width.</summary>
    public const string ZeroWidthMark = "·";

    /// <summary>Whether soft hyphens (U+00AD) are marked.</summary>
    public bool ShowSoftHyphens { get; set; }

    /// <summary>Whether non-breaking spaces (U+00A0) are marked.</summary>
    public bool ShowNonBreakingSpaces { get; set; }

    /// <summary>Whether the characters of zero width are marked.</summary>
    public bool ShowZeroWidth { get; set; }

    /// <summary>The look the marks are drawn in.</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

    /// <summary>
    /// The font of the marks (Preferences › Code Editor › Appearance › Fonts). What it leaves unset is the editor's
    /// own — the mark then sits in the line like any other character.
    /// </summary>
    public InvisibleCharacterAppearance Font { get; set; } = InvisibleCharacterAppearance.Default;

    /// <summary>Whether the character is one of those this generator marks, with the settings as they are now.</summary>
    public bool IsMarked(char c) => MarkFor(c) is not null;

    /// <summary>
    /// The mark that stands for <paramref name="c"/>, or <see langword="null"/> when the character is drawn as it is
    /// (or its kind is switched off).
    /// </summary>
    public string? MarkFor(char c) => c switch
    {
        '­' => ShowSoftHyphens ? SoftHyphenMark : null,
        ' ' => ShowNonBreakingSpaces ? NonBreakingSpaceMark : null,
        '​' or '‌' or '‍' or '⁠' or '﻿' => ShowZeroWidth ? ZeroWidthMark : null,
        _ => null,
    };

    /// <inheritdoc />
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!ShowSoftHyphens && !ShowNonBreakingSpaces && !ShowZeroWidth)
        {
            return -1;
        }

        TextDocument document = CurrentContext.Document;
        int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        for (int offset = startOffset; offset < endOffset; offset++)
        {
            if (IsMarked(document.GetCharAt(offset)))
            {
                return offset;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public override VisualLineElement? ConstructElement(int offset)
    {
        if (MarkFor(CurrentContext.Document.GetCharAt(offset)) is not { } mark)
        {
            return null;
        }

        return new InvisibleCharacterElement(
            mark,
            Theme.Foreground(ThemeKeys.EditorInvisibleCharacters) is { } color ? new SolidColorBrush(color) : null,
            Theme.Background(ThemeKeys.EditorInvisibleCharacters) is { } background ? new SolidColorBrush(background) : null,
            Font);
    }

    /// <summary>
    /// The mark of one character. Its look is set while the text run is made, not while the element is: the element's
    /// <see cref="VisualLineElement.TextRunProperties"/> are given to it only after the generator has returned it.
    /// </summary>
    private sealed class InvisibleCharacterElement : FormattedTextElement
    {
        private readonly IBrush? _foreground;
        private readonly IBrush? _background;
        private readonly InvisibleCharacterAppearance _font;

        public InvisibleCharacterElement(string mark, IBrush? foreground, IBrush? background, InvisibleCharacterAppearance font)
            : base(mark, documentLength: 1)
        {
            _foreground = foreground;
            _background = background;
            _font = font;
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            if (_foreground is not null)
            {
                TextRunProperties.SetForegroundBrush(_foreground);
            }

            if (_background is not null)
            {
                BackgroundBrush = _background;
            }

            // A font setting left unset keeps what the editor draws its text with (the run properties come with it).
            if (_font.FontFamily.Length > 0 || _font.FontWeight > 0)
            {
                Typeface current = TextRunProperties.Typeface;
                TextRunProperties.SetTypeface(new Typeface(
                    _font.FontFamily.Length > 0 ? new FontFamily(_font.FontFamily) : current.FontFamily,
                    current.Style,
                    _font.FontWeight > 0 ? FontWeightOptions.ToAvalonia(_font.FontWeight) : current.Weight,
                    current.Stretch));
            }

            if (_font.FontSize > 0)
            {
                TextRunProperties.SetFontRenderingEmSize(_font.FontSize);
            }

            return base.CreateTextRun(startVisualColumn, context);
        }
    }

    /// <summary>Reads the three settings of Code Editor; returns whether any of them changed.</summary>
    public bool ApplySettings(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool softHyphens = settings.CodeEditorShowSoftHyphens;
        bool nonBreakingSpaces = settings.CodeEditorShowNonBreakingSpaces;
        bool zeroWidth = settings.CodeEditorShowZeroWidth;
        if (softHyphens == ShowSoftHyphens && nonBreakingSpaces == ShowNonBreakingSpaces && zeroWidth == ShowZeroWidth)
        {
            return false;
        }

        ShowSoftHyphens = softHyphens;
        ShowNonBreakingSpaces = nonBreakingSpaces;
        ShowZeroWidth = zeroWidth;
        return true;
    }
}
