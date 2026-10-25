using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// A piece of CSS shown outside Code Editor (a declaration or a selector of the Live CSS panel), colored by the
/// editor's CSS highlighter in the colors of the current color scheme. Plain text when
/// <see cref="CssText"/> is empty.
/// </summary>
public sealed class CssSyntaxText : TextBlock
{
    /// <summary>The CSS to show.</summary>
    public static readonly StyledProperty<string?> CssTextProperty =
        AvaloniaProperty.Register<CssSyntaxText, string?>(nameof(CssText));

    /// <summary>Whether the text is a selector (not a declaration) — it is highlighted as one.</summary>
    public static readonly StyledProperty<bool> IsSelectorProperty =
        AvaloniaProperty.Register<CssSyntaxText, bool>(nameof(IsSelector));

    static CssSyntaxText()
    {
        CssTextProperty.Changed.AddClassHandler<CssSyntaxText>((t, _) => t.Rebuild());
        IsSelectorProperty.Changed.AddClassHandler<CssSyntaxText>((t, _) => t.Rebuild());
    }

    /// <inheritdoc cref="CssTextProperty"/>
    public string? CssText
    {
        get => GetValue(CssTextProperty);
        set => SetValue(CssTextProperty, value);
    }

    /// <inheritdoc cref="IsSelectorProperty"/>
    public bool IsSelector
    {
        get => GetValue(IsSelectorProperty);
        set => SetValue(IsSelectorProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed += OnThemeChanged;
        Rebuild(); // the theme may have changed while the control was away
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppTheme.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        InlineCollection inlines = new();
        if (CssText is { Length: > 0 } text)
        {
            ResolvedTheme theme = AppTheme.Current;
            foreach (UsageContextSegment segment in CssSyntaxSegments.Of(text, IsSelector))
            {
                inlines.Add(Run(segment, theme));
            }
        }

        Inlines = inlines;
    }

    private static Run Run(UsageContextSegment segment, ResolvedTheme theme)
    {
        Run run = new(segment.Text);
        if (segment.Format is { } format && SyntaxColorizer.SyntaxKeys.TryGetValue(format, out ThemeKey? key))
        {
            TextAttributes look = theme.Attributes(key);
            if (look.Foreground is { } foreground)
            {
                run.Foreground = new SolidColorBrush(ResolvedTheme.ToColor(foreground));
            }

            run.FontWeight = look.Bold ? FontWeight.Bold : FontWeight.Normal;
            run.FontStyle = look.Italic ? FontStyle.Italic : FontStyle.Normal;
        }

        return run;
    }
}
