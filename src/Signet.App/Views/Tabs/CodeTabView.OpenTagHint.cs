using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.Views.Tabs;

/// <summary>
/// The opening tag hint: when the mouse rests on a closing tag (<c>&lt;/div&gt;</c>) for the delay set in Preferences,
/// a tooltip shows its opening tag exactly as in the source, with its line number — in the font and colors from
/// Preferences (by default the editor font and the theme tooltip colors). A popup with
/// the tooltip look (not a <c>ToolTip</c>), so it does not interfere with the issue tooltips of the text view; an
/// issue at the closing tag takes precedence and the hint is not shown.
/// </summary>
public partial class CodeTabView
{
    private readonly DispatcherTimer _openTagHintTimer = new();

    // Offset of the closing tag under the mouse — pending (timer running) or shown; -1 when none.
    private int _openTagHintCloseOffset = -1;

    private void InitializeOpenTagHint()
    {
        _openTagHintTimer.Tick += (_, _) => ShowOpenTagHint();

        TextView textView = Editor.TextArea.TextView;
        textView.PointerMoved += OnOpenTagHintPointerMoved;
        textView.PointerExited += (_, _) => HideOpenTagHint();
        textView.ScrollOffsetChanged += (_, _) => HideOpenTagHint();
        textView.DocumentChanged += (_, _) => HideOpenTagHint();
        Editor.AddHandler(PointerPressedEvent, (_, _) => HideOpenTagHint(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Editor.AddHandler(KeyDownEvent, (_, _) => HideOpenTagHint(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Editor.AddHandler(PointerWheelChangedEvent, (_, _) => HideOpenTagHint(), RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => HideOpenTagHint();
        DetachedFromVisualTree += (_, _) => HideOpenTagHint();
    }

    private void OnOpenTagHintPointerMoved(object? sender, PointerEventArgs e)
    {
        int closeOffset = CloseTagUnderPointer(e.GetPosition(Editor.TextArea.TextView));
        if (closeOffset == _openTagHintCloseOffset)
        {
            // Still on the same closing tag: the hint stays (or keeps waiting).
            return;
        }

        HideOpenTagHint();
        if (closeOffset < 0 || _boundViewModel is not { } vm)
        {
            return;
        }

        _openTagHintCloseOffset = closeOffset;
        int delay = vm.OpenTagHintDelayMs;
        if (delay <= 0)
        {
            ShowOpenTagHint();
            return;
        }

        _openTagHintTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _openTagHintTimer.Start();
    }

    /// <summary>The offset of the closing tag under <paramref name="point"/> (text view coordinates), or -1.</summary>
    private int CloseTagUnderPointer(Point point)
    {
        TextView textView = Editor.TextArea.TextView;
        if (_boundViewModel is not { OpenTagHintEnabled: true } vm
            || textView.Document is not { } document
            || textView.GetPositionFloor(point + textView.ScrollOffset) is not { } position)
        {
            return -1;
        }

        return vm.GetCloseTagAt(document.GetOffset(position.Location))?.Offset ?? -1;
    }

    private void ShowOpenTagHint()
    {
        _openTagHintTimer.Stop();
        TextDocument? document = Editor.Document;
        if (_boundViewModel is not { OpenTagHintEnabled: true } vm
            || document is null
            || _openTagHintCloseOffset < 0
            || vm.GetCloseTagAt(_openTagHintCloseOffset) is not { } close
            || HasIssueWithin(document, close.Offset, close.Length)
            || vm.GetOpenTagOfCloseTag(close.Offset) is not { } open
            || open.Offset + open.Length > document.TextLength)
        {
            return;
        }

        OpenTagHintAppearance look = vm.OpenTagHintAppearance;
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush foreground = new SolidColorBrush(Color.Parse(look.ForegroundFor(dark)));
        OpenTagHintBorder.Background = new SolidColorBrush(Color.Parse(look.BackgroundFor(dark)));
        OpenTagHintTag.Foreground = foreground;
        OpenTagHintLine.Foreground = foreground;
        OpenTagHintTag.Text = document.GetText(open.Offset, open.Length);
        OpenTagHintTag.FontFamily = look.FontFamily.Length > 0 ? new FontFamily(look.FontFamily) : Editor.FontFamily;
        OpenTagHintTag.FontSize = look.FontSize > 0 ? look.FontSize : Editor.FontSize;
        OpenTagHintLine.Text = Strings.Format("CodeView_OpenTagHintLine", document.GetLineByOffset(open.Offset).LineNumber);
        OpenTagHintPopup.IsOpen = true;
    }

    // An issue tooltip (error, broken link, warning) anywhere on the closing tag takes precedence over the hint.
    private bool HasIssueWithin(TextDocument document, int offset, int length)
    {
        for (int i = offset; i < offset + length && i < document.TextLength; i++)
        {
            if (IssueTooltipAt(document, i) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private void HideOpenTagHint()
    {
        _openTagHintTimer.Stop();
        _openTagHintCloseOffset = -1;
        OpenTagHintPopup.IsOpen = false;
    }
}
