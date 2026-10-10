using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>Ctrl+V in Code View: the clipboard text is normalized to Unicode NFC when Preferences say so.</summary>
public sealed class CodeTabViewPasteTests
{
    // "cafe" + U+0301 (combining acute), built from code points so that no editor can normalize it.
    private static readonly string Decomposed = "cafe" + (char)0x0301;

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ctrl_V_pastes_the_clipboard_text_normalized_when_enabled(bool enabled)
    {
        using TempDir temp = new();
        var html = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        html.SetText("<p></p>");
        SettingsStore settings = new(temp.Combine("settings.json")) { CodeViewPasteNormalizeNfc = enabled };
        SpellChecker spellChecker = new(settings, temp.Combine("hunspell"), temp.Combine("user"));
        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 600, Height = 300, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Settle(window);
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        editor.CaretOffset = "<p>".Length;
        await window.Clipboard!.SetTextAsync(Decomposed);

        window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
        Settle(window);

        editor.Document.Text.Should().Be("<p>" + (enabled ? "caf" + (char)0x00E9 : Decomposed) + "</p>");
        window.Close();
    }
}
