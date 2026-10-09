using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// The notification bell (the Notifications tab title, the status bar indicator): drawn in the text color; while
/// there are unread error notifications (<see cref="UnreadCount"/> &gt; 0) a red dot sits on the bell and the count
/// follows it as <c>[n]</c>.
/// </summary>
public partial class NotificationBell : UserControl
{
    /// <summary>Defines the <see cref="UnreadCount"/> property.</summary>
    public static readonly StyledProperty<int> UnreadCountProperty =
        AvaloniaProperty.Register<NotificationBell, int>(nameof(UnreadCount));

    /// <summary>Initializes the control.</summary>
    public NotificationBell()
    {
        InitializeComponent();
    }

    /// <summary>The number of unread error notifications; 0 = just the bell.</summary>
    public int UnreadCount
    {
        get => GetValue(UnreadCountProperty);
        set => SetValue(UnreadCountProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UnreadCountProperty)
        {
            int count = change.GetNewValue<int>();
            Dot.IsVisible = count > 0;
            Count.IsVisible = count > 0;
            Count.Text = count > 0 ? "[" + count.ToString(CultureInfo.CurrentCulture) + "]" : string.Empty;
        }
    }
}
