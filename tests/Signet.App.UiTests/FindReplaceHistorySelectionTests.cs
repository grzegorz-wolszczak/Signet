using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// An entry picked from the Find / Replace field history survives running the operation: reordering
/// the history (the picked entry moves to the top) must not bring back the previously typed text.
/// </summary>
public sealed class FindReplaceHistorySelectionTests
{
    [AvaloniaTheory]
    [InlineData("FindBox")]
    [InlineData("ReplaceBox")]
    public void Text_picked_from_history_stays_in_the_field_after_the_operation(string boxName)
    {
        using TempDir temp = new();
        CodeTabViewModel tab = NewCodeTab(temp.Path, "<p>test foo bar</p>");
        FindReplaceViewModel vm = new(
            new SettingsStore(Path.Combine(temp.Path, "settings.json")),
            new StatusBarService(),
            () => tab,
            Mock.Of<IMultiFileSearchHost>());
        vm.AttachToActiveTab(tab);
        string[] history = ["test", "foo", "bar"];
        foreach (string item in history)
        {
            vm.FindHistory.Add(item);
            vm.ReplaceHistory.Add(item);
        }

        vm.FindText = "foo";
        Window window = new() { Width = 900, Height = 400, Content = new FindReplaceView { DataContext = vm } };
        window.Show();
        Pump(window);
        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == boxName);

        // Manually typed text: AutoCompleteBox remembers it as SearchText.
        Click(window, box.GetVisualDescendants().OfType<TextBox>().First());
        box.Text = string.Empty;
        window.KeyTextInput("test");
        Pump(window);

        // ▾ → click "bar" in the history list.
        box.GetVisualDescendants().OfType<Button>().Single(b => b.Name == boxName + "HistoryButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(window);
        ListBox list = box.GetVisualDescendants().OfType<Popup>().Single().Child!
            .GetVisualDescendants().OfType<ListBox>().Single();
        ListBoxItem barItem = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => Equals(i.Content, "bar"));
        Click(TopLevel.GetTopLevel(barItem)!, barItem);
        Pump(window);
        box.Text.Should().Be("bar");

        System.Windows.Input.ICommand command = boxName == "FindBox" ? vm.FindNextCommand : vm.ReplaceFindCommand;
        Click(window, window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, command)));
        Pump(window);

        box.Text.Should().Be("bar");
        (boxName == "FindBox" ? vm.FindText : vm.ReplaceText).Should().Be("bar");
        (boxName == "FindBox" ? vm.FindHistory : vm.ReplaceHistory).First().Should().Be("bar");
        window.Close();
    }

    private static CodeTabViewModel NewCodeTab(string dir, string xhtml)
    {
        string path = Path.Combine(dir, "Section0001.xhtml");
        File.WriteAllText(path, xhtml);
        HtmlResource html = new(dir, path);
        html.InitialLoad();
        SettingsStore settings = new(Path.Combine(dir, "tab-settings.json"));
        SpellChecker spellChecker = new(
            settings,
            Path.Combine(dir, "hunspell_dictionaries"),
            Path.Combine(dir, "user_dictionaries"));
        return new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    private static void Click(TopLevel root, Visual target)
    {
        Point point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException("Target is not in the given top level.");
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
