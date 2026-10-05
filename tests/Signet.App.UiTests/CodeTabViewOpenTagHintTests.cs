using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The opening tag hint of Code View: resting the mouse on a closing tag shows its opening tag with the line number,
/// after the delay from Preferences, and not at all when the hint is switched off.
/// </summary>
public sealed class CodeTabViewOpenTagHintTests
{
    private const string OpenTag = "<div class=\"chapter\"\n     id=\"ch1\">";

    private const string Xhtml =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<body>\n" + OpenTag + "\n<p>text</p>\n</div>\n</body>\n</html>\n";

    private sealed record Host(Window Window, CodeTabView View, TextEditor Editor, TempDir Temp) : IDisposable
    {
        public Popup Popup => View.FindControl<Popup>("OpenTagHintPopup")!;

        public void Dispose()
        {
            Window.Close();
            Temp.Dispose();
        }
    }

    private static Host Show(Action<SettingsStore> configure)
    {
        TempDir temp = new();
        var html = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        html.SetText(Xhtml);
        OpenTab tab = new TabManagerModel().OpenResource(html);
        SettingsStore settings = new(temp.Combine("settings.json"));
        configure(settings);
        SpellChecker spellChecker = new(settings, temp.Combine("hunspell"), temp.Combine("user"));
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        var view = new CodeTabView { DataContext = vm };
        var window = new Window { Width = 800, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return new Host(window, view, window.GetVisualDescendants().OfType<TextEditor>().Single(), temp);
    }

    // Moves the mouse over the middle of the character at the given offset.
    private static void HoverOffset(Host host, int offset)
    {
        TextView textView = host.Editor.TextArea.TextView;
        TextViewPosition position = new(host.Editor.Document.GetLocation(offset));
        Point start = textView.GetVisualPosition(position, VisualYPosition.LineMiddle) - textView.ScrollOffset;
        Point point = new(start.X + (textView.WideSpaceWidth / 2), start.Y);
        host.Window.MouseMove(textView.TranslatePoint(point, host.Window)!.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static int CloseTagOffset => Xhtml.IndexOf("</div>", StringComparison.Ordinal);

    [AvaloniaFact]
    public void Resting_on_a_closing_tag_shows_its_opening_tag_with_the_line_number()
    {
        using Host host = Show(settings => settings.CodeViewOpenTagHintDelayMs = 0);

        HoverOffset(host, CloseTagOffset + 3);

        host.Popup.IsOpen.Should().BeTrue();
        host.View.FindControl<TextBlock>("OpenTagHintTag")!.Text.Should().Be(OpenTag);
        host.View.FindControl<TextBlock>("OpenTagHintTag")!.FontFamily.Should().Be(host.Editor.FontFamily);
        host.View.FindControl<TextBlock>("OpenTagHintLine")!.Text.Should().Be(Strings.Format("CodeView_OpenTagHintLine", 3));

        HoverOffset(host, Xhtml.IndexOf("text", StringComparison.Ordinal));

        host.Popup.IsOpen.Should().BeFalse("the mouse left the closing tag");
    }

    [AvaloniaFact]
    public void The_hint_uses_the_font_and_colors_from_Preferences()
    {
        using Host host = Show(settings =>
        {
            settings.CodeViewOpenTagHintDelayMs = 0;
            settings.OpenTagHintAppearance = new OpenTagHintAppearance("Courier New", 19, "#112233", "#445566", "#778899", "#aabbcc");
        });

        HoverOffset(host, CloseTagOffset + 3);

        bool dark = host.View.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        TextBlock tag = host.View.FindControl<TextBlock>("OpenTagHintTag")!;
        tag.FontFamily.Name.Should().Be("Courier New");
        tag.FontSize.Should().Be(19);
        ((Avalonia.Media.ISolidColorBrush)tag.Foreground!).Color.Should().Be(Avalonia.Media.Color.Parse(dark ? "#aabbcc" : "#445566"));
        ((Avalonia.Media.ISolidColorBrush)host.View.FindControl<Border>("OpenTagHintBorder")!.Background!).Color
            .Should().Be(Avalonia.Media.Color.Parse(dark ? "#778899" : "#112233"));
    }

    [AvaloniaFact]
    public void The_hint_waits_for_the_delay()
    {
        using Host host = Show(settings => settings.CodeViewOpenTagHintDelayMs = 150);

        HoverOffset(host, CloseTagOffset + 3);

        host.Popup.IsOpen.Should().BeFalse("the delay has not passed yet");

        // RunJobs does not run timers — run the dispatcher loop for a while, as the application does.
        DispatcherFrame frame = new();
        DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(400));
        Dispatcher.UIThread.PushFrame(frame);

        host.Popup.IsOpen.Should().BeTrue("the delay has passed");
    }

    [AvaloniaFact]
    public void A_switched_off_hint_never_shows()
    {
        using Host host = Show(settings =>
        {
            settings.CodeViewOpenTagHint = false;
            settings.CodeViewOpenTagHintDelayMs = 0;
        });

        HoverOffset(host, CloseTagOffset + 3);

        host.Popup.IsOpen.Should().BeFalse();
    }
}
