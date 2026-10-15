using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Signet.App.Infrastructure;

/// <summary>
/// A brief hint in the middle of a control that shows the font size just set (Ctrl + wheel in the code editors), so the
/// size is known while it is being changed. It stays for a moment after the last change, fades out and goes. It is drawn
/// in the adorner layer over the control and does not take the mouse.
/// </summary>
public static class ZoomHint
{
    /// <summary>Whether the hint is shown at all (the "show the font size" preference); on by default.</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly TimeSpan s_visible = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan s_fade = TimeSpan.FromMilliseconds(180);
    private static readonly ConditionalWeakTable<Control, State> s_states = new();

    /// <summary>Shows (or updates) the hint over <paramref name="target"/>; does nothing when it has no adorner layer.</summary>
    public static void Show(Control target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        if (!Enabled || AdornerLayer.GetAdornerLayer(target) is not { } layer)
        {
            return;
        }

        State state = s_states.GetValue(target, _ => new State());
        if (state.Hint is null || !layer.Children.Contains(state.Hint))
        {
            state.Text = new TextBlock { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeight.SemiBold };
            state.Hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = state.Text,
            };
            AdornerLayer.SetAdornedElement(state.Hint, target);
            AdornerLayer.SetIsClipEnabled(state.Hint, false);
            layer.Children.Add(state.Hint);
        }

        state.Text!.Text = text;
        state.Hint.Opacity = 1;
        state.Restart(layer);
    }

    private sealed class State
    {
        private DispatcherTimer? _timer;
        private bool _fading;

        public Border? Hint { get; set; }

        public TextBlock? Text { get; set; }

        public void Restart(AdornerLayer layer)
        {
            _timer?.Stop();
            _fading = false;
            _timer = new DispatcherTimer { Interval = s_visible };
            _timer.Tick += (_, _) =>
            {
                if (!_fading)
                {
                    // Fade out (the opacity follows at once when the platform has no transition), then remove.
                    _fading = true;
                    Hint!.Transitions = new Avalonia.Animation.Transitions
                    {
                        new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = s_fade },
                    };
                    Hint.Opacity = 0;
                    _timer!.Interval = s_fade + TimeSpan.FromMilliseconds(40);
                    return;
                }

                _timer!.Stop();
                layer.Children.Remove(Hint!);
                Hint!.Transitions = null;
            };
            _timer.Start();
        }
    }
}
