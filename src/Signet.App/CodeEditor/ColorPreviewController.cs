using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.Core.Parsers;

namespace Signet.App.CodeEditor;

/// <summary>What a Code Editor tab holds, for the color preview.</summary>
public enum ColorPreviewKind
{
    /// <summary>Neither CSS nor (X)HTML: no colors are looked for.</summary>
    None,

    /// <summary>A stylesheet.</summary>
    Css,

    /// <summary>An (X)HTML file (its <c>&lt;style&gt;</c> elements and <c>style</c> attributes).</summary>
    Html,
}

/// <summary>
/// The color preview of the Code Editor (Calibre 9ec8e99d48, 86546c1069, 776db6a5c9): an underline in its own color under every color value of a
/// stylesheet or of the style of an (X)HTML file, and the replacement of a color chosen in the picker (a Ctrl+click on it). The colors are
/// looked for (<see cref="CssColorScanner"/>) on a background thread a moment after the text stopped changing; until then the old underlines
/// are not drawn (their offsets are old), and a file too long for that is not scanned at all. Only the visible lines are drawn.
/// </summary>
public sealed class ColorPreviewController : IDisposable
{
    /// <summary>A file longer than this many characters is not scanned.</summary>
    public const int MaxScannedLength = 2_000_000;

    private const int DebounceMilliseconds = 400;
    private const int UnderlineThickness = 3;

    private static readonly IReadOnlyList<CssColorMatch> NoMatches = Array.Empty<CssColorMatch>();

    private readonly TextEditor _editor;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
    private readonly Renderer _renderer;
    private TextDocument? _document;
    private IReadOnlyList<CssColorMatch> _matches = NoMatches;
    private bool _enabled;
    private ColorPreviewKind _kind;
    private int _version;

    /// <summary>Creates the controller for <paramref name="editor"/> and puts its renderer into the text view.</summary>
    public ColorPreviewController(TextEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _renderer = new Renderer(this);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        _editor.DocumentChanged += OnDocumentChanged;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            ScanInBackground();
        };
        Attach(_editor.Document);
    }

    /// <summary>Whether the preview is on (the setting of Preferences).</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                Reset();
            }
        }
    }

    /// <summary>What the tab holds.</summary>
    public ColorPreviewKind Kind
    {
        get => _kind;
        set
        {
            if (_kind != value)
            {
                _kind = value;
                Reset();
            }
        }
    }

    /// <summary>The colors found by the last look, in text order (stale after an edit: see <see cref="IsStale"/>).</summary>
    public IReadOnlyList<CssColorMatch> Matches => _matches;

    /// <summary>Whether the text changed since the last look (the offsets of <see cref="Matches"/> are old).</summary>
    public bool IsStale { get; private set; }

    /// <summary>The color value that contains <paramref name="offset"/>, or <c>null</c> (also while the colors are stale).</summary>
    public CssColorMatch? MatchAt(int offset)
    {
        if (IsStale || !_enabled)
        {
            return null;
        }

        foreach (CssColorMatch match in _matches)
        {
            if (offset >= match.Start && offset < match.Start + match.Length)
            {
                return match;
            }

            if (match.Start > offset)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Looks for the colors now, on this thread.</summary>
    public void RefreshNow()
    {
        _timer.Stop();
        _version++;
        IReadOnlyList<CssColorMatch> found = Scannable ? Scan(_editor.Document.Text, _kind) : NoMatches;
        Accept(found);
    }

    /// <summary>
    /// Replaces the color value <paramref name="match"/> with <paramref name="color"/> written in the notation it had; one step of the undo.
    /// </summary>
    public void ApplyColor(CssColorMatch match, CssColor color)
    {
        ArgumentNullException.ThrowIfNull(match);
        TextDocument document = _editor.Document;
        string text = document.Text;
        string replacement = CssColorScanner.Format(color, match, text);
        document.Replace(match.Start, match.Length, replacement);
        RefreshNow();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Stop();
        _editor.DocumentChanged -= OnDocumentChanged;
        Detach();
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
    }

    private bool Scannable => _enabled && _kind != ColorPreviewKind.None && _editor.Document is { TextLength: <= MaxScannedLength };

    private static IReadOnlyList<CssColorMatch> Scan(string text, ColorPreviewKind kind) =>
        kind == ColorPreviewKind.Css ? CssColorScanner.InStylesheet(text) : CssColorScanner.InHtml(text);

    private void Accept(IReadOnlyList<CssColorMatch> found)
    {
        _matches = found;
        IsStale = false;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    // The setting or the kind changed: what was found is no longer what is wanted.
    private void Reset()
    {
        if (!Scannable)
        {
            _timer.Stop();
            _version++;
            Accept(NoMatches);
        }
        else
        {
            IsStale = true;
            Schedule();
        }
    }

    private void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void ScanInBackground()
    {
        if (!Scannable)
        {
            return;
        }

        int version = ++_version;
        string text = _editor.Document.Text;
        ColorPreviewKind kind = _kind;
        Task.Run(() => Scan(text, kind)).ContinueWith(
            task =>
            {
                if (version == _version && task.IsCompletedSuccessfully)
                {
                    Accept(task.Result);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Detach();
        Attach(_editor.Document);
        Reset();
    }

    private void Attach(TextDocument? document)
    {
        _document = document;
        if (document is not null)
        {
            document.TextChanged += OnTextChanged;
        }
    }

    private void Detach()
    {
        if (_document is not null)
        {
            _document.TextChanged -= OnTextChanged;
            _document = null;
        }
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _version++;
        if (!_enabled || _kind == ColorPreviewKind.None)
        {
            return;
        }

        IsStale = true;
        Schedule();
    }

    // The underlines: in the layer under the text, in the color of the value, only for the lines that are shown.
    private sealed class Renderer(ColorPreviewController owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            ArgumentNullException.ThrowIfNull(textView);
            ArgumentNullException.ThrowIfNull(drawingContext);
            if (owner.IsStale || !owner._enabled || owner._matches.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            {
                return;
            }

            int from = textView.VisualLines[0].FirstDocumentLine.Offset;
            int to = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            foreach (CssColorMatch match in owner._matches.Where(m => m.Color is not null && m.Start + m.Length > from && m.Start < to))
            {
                CssColor color = match.Color!.Value;
                IBrush brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
                TextSegment segment = new() { StartOffset = match.Start, Length = match.Length };
                foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.FillRectangle(brush, new Rect(rect.X, rect.Bottom - UnderlineThickness, rect.Width, UnderlineThickness));
                }
            }
        }
    }
}
