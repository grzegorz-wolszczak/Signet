using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The "Context" cell of the "Find Usages" panel: the line of the usage colored by the syntax highlighter of Code Editor
/// (in the colors of the color scheme) with the searched text marked the way Find marks its matches
/// (<c>search.match</c>). Plain, uncolored text when the node has no usage.
/// </summary>
public sealed class UsageContextText : TextBlock
{
    /// <summary>The node whose context is shown.</summary>
    public static readonly StyledProperty<FindUsagesNode?> NodeProperty =
        AvaloniaProperty.Register<UsageContextText, FindUsagesNode?>(nameof(Node));

    static UsageContextText()
    {
        NodeProperty.Changed.AddClassHandler<UsageContextText>((t, _) => t.Rebuild());
    }

    /// <inheritdoc cref="NodeProperty"/>
    public FindUsagesNode? Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed += OnThemeChanged;
        Rebuild(); // the theme may have changed while the control (a recycled cell) was away
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
        if (Node?.Usage is { } usage)
        {
            ResolvedTheme theme = AppTheme.Current;
            TextAttributes match = theme.Attributes(ThemeKeys.SearchMatch);
            foreach (UsageContextSegment segment in UsageContextSegments.Of(usage))
            {
                inlines.Add(Run(segment, theme, match));
            }
        }

        Inlines = inlines;
    }

    private static Run Run(UsageContextSegment segment, ResolvedTheme theme, TextAttributes match)
    {
        Run run = new(segment.Text);
        if (segment.Format is { } format && SyntaxColorizer.SyntaxKeys.TryGetValue(format, out ThemeKey? key))
        {
            TextAttributes look = theme.Attributes(key);
            run.Foreground = Brush(look.Foreground);
            run.Background = Brush(look.Background);
            run.FontWeight = look.Bold ? FontWeight.Bold : FontWeight.Normal;
            run.FontStyle = look.Italic ? FontStyle.Italic : FontStyle.Normal;
        }

        if (segment.IsMatch)
        {
            run.Background = Brush(match.Background) ?? run.Background;
            if (Brush(match.Foreground) is { } foreground)
            {
                run.Foreground = foreground;
            }
        }

        return run;
    }

    private static SolidColorBrush? Brush(ThemeColor? color) => color is { } c ? new SolidColorBrush(ResolvedTheme.ToColor(c)) : null;
}
