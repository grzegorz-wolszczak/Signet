using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The text of a node of the "Bookmarks" panel: a file as its plain text, a bookmark as its line number followed by the line,
/// colored by the syntax highlighter of Code Editor in the colors of the color scheme (like the "Context" of Find Usages).
/// </summary>
public sealed class BookmarkNodeText : TextBlock
{
    /// <summary>The node whose text is shown.</summary>
    public static readonly StyledProperty<BookmarkNode?> NodeProperty =
        AvaloniaProperty.Register<BookmarkNodeText, BookmarkNode?>(nameof(Node));

    static BookmarkNodeText()
    {
        NodeProperty.Changed.AddClassHandler<BookmarkNodeText>((t, _) => t.Rebuild());
    }

    /// <inheritdoc cref="NodeProperty"/>
    public BookmarkNode? Node
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
        if (Node is { } node)
        {
            if (node.Bookmark is null)
            {
                inlines.Add(new Run(node.Text));
            }
            else
            {
                inlines.Add(new Run(node.Prefix));
                ResolvedTheme theme = AppTheme.Current;
                TextAttributes match = theme.Attributes(ThemeKeys.SearchMatch);
                foreach (UsageContextSegment segment in UsageContextSegments.Of(node.LineText, node.Syntax))
                {
                    inlines.Add(UsageContextText.Run(segment, theme, match));
                }
            }
        }

        Inlines = inlines;
    }
}
