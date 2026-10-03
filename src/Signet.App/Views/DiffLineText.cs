using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Signet.Core.Diff;

namespace Signet.App.Views;

/// <summary>
/// A diff line in the "Compare" window: the background of the whole line by kind of change (deleted —
/// red, inserted — green, empty padding cell — gray) and a stronger background for the changed
/// words in a changed line.
/// </summary>
public sealed class DiffLineText : TextBlock
{
    /// <summary>Text fragments of the line.</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffSpan>?> SpansProperty =
        AvaloniaProperty.Register<DiffLineText, IReadOnlyList<DiffSpan>?>(nameof(Spans));

    /// <summary>Line kind.</summary>
    public static readonly StyledProperty<DiffLineKind> KindProperty =
        AvaloniaProperty.Register<DiffLineText, DiffLineKind>(nameof(Kind));

    /// <summary>
    /// Whether the line is on the left side (the checkpoint state) — a changed line is then
    /// colored like a deleted one, and on the right like an inserted one.
    /// </summary>
    public static readonly StyledProperty<bool> IsLeftProperty =
        AvaloniaProperty.Register<DiffLineText, bool>(nameof(IsLeft));

    private static readonly IBrush DeletedLine = new SolidColorBrush(Color.FromArgb(0x38, 0xE5, 0x39, 0x35));
    private static readonly IBrush InsertedLine = new SolidColorBrush(Color.FromArgb(0x38, 0x43, 0xA0, 0x47));
    private static readonly IBrush DeletedWord = new SolidColorBrush(Color.FromArgb(0x80, 0xE5, 0x39, 0x35));
    private static readonly IBrush InsertedWord = new SolidColorBrush(Color.FromArgb(0x80, 0x43, 0xA0, 0x47));
    private static readonly IBrush EmptyCell = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));

    static DiffLineText()
    {
        SpansProperty.Changed.AddClassHandler<DiffLineText>((t, _) => t.Rebuild());
        KindProperty.Changed.AddClassHandler<DiffLineText>((t, _) => t.Rebuild());
        IsLeftProperty.Changed.AddClassHandler<DiffLineText>((t, _) => t.Rebuild());
    }

    /// <summary>Initializes the control (monospace, wrapped text).</summary>
    public DiffLineText()
    {
        TextWrapping = TextWrapping.Wrap;
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
        Padding = new Thickness(4, 1);
    }

    /// <inheritdoc cref="SpansProperty"/>
    public IReadOnlyList<DiffSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    /// <inheritdoc cref="KindProperty"/>
    public DiffLineKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <inheritdoc cref="IsLeftProperty"/>
    public bool IsLeft
    {
        get => GetValue(IsLeftProperty);
        set => SetValue(IsLeftProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    private void Rebuild()
    {
        bool deletedSide = Kind == DiffLineKind.Deleted || (Kind == DiffLineKind.Modified && IsLeft);
        Background = Kind switch
        {
            DiffLineKind.Unchanged => null,
            DiffLineKind.Empty => EmptyCell,
            _ => deletedSide ? DeletedLine : InsertedLine,
        };

        InlineCollection inlines = new();
        foreach (DiffSpan span in Spans ?? Array.Empty<DiffSpan>())
        {
            Run run = new(span.Text);
            if (span.Changed)
            {
                run.Background = deletedSide ? DeletedWord : InsertedWord;
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
