using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Signet.App.Infrastructure;

namespace Signet.App.CodeEditor;

/// <summary>
/// Ctrl + mouse wheel changes the font size of the code editors of the Preferences window (the CSS fields of Footnotes,
/// the code samples of the Fonts and Color Scheme pages). One shared <see cref="Delta"/> (in pixels, on top of each
/// editor's own base size) applies to all of them, so the editors stay consistent; the main window remembers it between
/// runs, like the sizes of the panes.
/// </summary>
public static class EditorZoom
{
    /// <summary>The smallest and the largest font size an editor can be zoomed to (px).</summary>
    public const double MinSize = 6, MaxSize = 72;

    /// <summary>The range of <see cref="Delta"/>.</summary>
    public const int MinDelta = -12, MaxDelta = 48;

    /// <summary>The base font size of an editor (what it would have without the zoom).</summary>
    public static readonly AttachedProperty<double> BaseSizeProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, double>("BaseSize", typeof(EditorZoom), 13);

    private static readonly List<WeakReference<TextEditor>> s_editors = new();
    private static int s_delta;

    static EditorZoom() => BaseSizeProperty.Changed.AddClassHandler<TextEditor>((editor, _) => Apply(editor));

    /// <summary>Raised when <see cref="Delta"/> changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>The zoom in pixels (negative = smaller); clamped to <see cref="MinDelta"/>..<see cref="MaxDelta"/>.</summary>
    public static int Delta
    {
        get => s_delta;
        set
        {
            int clamped = Math.Clamp(value, MinDelta, MaxDelta);
            if (clamped == s_delta)
            {
                return;
            }

            s_delta = clamped;
            ApplyAll();
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The size an editor with the base size <paramref name="baseSize"/> has at the current zoom.</summary>
    public static double SizeFor(double baseSize) => Math.Clamp(baseSize + s_delta, MinSize, MaxSize);

    /// <summary>Gets the base size of an editor.</summary>
    public static double GetBaseSize(TextEditor editor) => editor.GetValue(BaseSizeProperty);

    /// <summary>Sets the base size of an editor; its font size becomes the base size at the current zoom.</summary>
    public static void SetBaseSize(TextEditor editor, double size) => editor.SetValue(BaseSizeProperty, size);

    /// <summary>
    /// Makes Ctrl + wheel over <paramref name="editor"/> change the shared zoom, and keeps the editor's font size in step
    /// with it (its current font size is its base size).
    /// </summary>
    public static void Attach(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        s_editors.Add(new WeakReference<TextEditor>(editor));
        editor.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        SetBaseSize(editor, editor.FontSize);
        Apply(editor);
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        Delta += e.Delta.Y > 0 ? 1 : -1;
        if (sender is TextEditor editor)
        {
            ZoomHint.Show(editor, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{editor.FontSize:0.#} px"));
        }

        e.Handled = true;
    }

    private static void Apply(TextEditor editor)
    {
        double size = SizeFor(GetBaseSize(editor));
        if (Math.Abs(editor.FontSize - size) > 0.001)
        {
            editor.FontSize = size;
        }
    }

    private static void ApplyAll()
    {
        s_editors.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (WeakReference<TextEditor> reference in s_editors.ToArray())
        {
            if (reference.TryGetTarget(out TextEditor? editor))
            {
                Apply(editor);
            }
        }
    }
}
