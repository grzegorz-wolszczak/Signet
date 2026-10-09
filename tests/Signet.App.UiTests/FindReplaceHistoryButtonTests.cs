using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.MainUI;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>The ▾ buttons in the Find / Replace fields drop down the history.</summary>
public sealed class FindReplaceHistoryButtonTests
{
    [AvaloniaTheory]
    [InlineData("FindBox")]
    [InlineData("ReplaceBox")]
    public void History_button_opens_the_whole_history_even_when_the_field_has_text(string boxName)
    {
        using TempDir temp = new();
        FindReplaceViewModel vm = new(
            new SettingsStore(Path.Combine(temp.Path, "settings.json")),
            new StatusBarService(),
            () => (CodeTabViewModel?)null,
            Mock.Of<IMultiFileSearchHost>());
        string[] history = ["w pociąg", "że sobowtór", "Dotykałem ich"];
        foreach (string item in history)
        {
            vm.FindHistory.Add(item);
            vm.ReplaceHistory.Add(item);
        }

        vm.FindText = "nic wspólnego";
        vm.ReplaceText = "nic wspólnego";
        Window window = new() { Width = 900, Height = 400, Content = new FindReplaceView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == boxName);
        Button button = box.GetVisualDescendants().OfType<Button>().Single(b => b.Name == boxName + "HistoryButton");
        button.IsEffectivelyVisible.Should().BeTrue();

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        box.IsDropDownOpen.Should().BeTrue();
        Popup popup = box.GetVisualDescendants().OfType<Popup>().Single();
        popup.IsOpen.Should().BeTrue();
        ListBox list = popup.Child!.GetVisualDescendants().Prepend(popup.Child).OfType<ListBox>().Single();
        list.Items.Cast<object>().Should().Equal(history);
        window.Close();
    }

    /// <summary>
    /// Regression: on Find Next, the entry picked from the history (the field's SelectedItem) was
    /// moved to the top of the history by removing and re-inserting it, which made AutoCompleteBox
    /// clear the Find field.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(2)]
    public void Find_next_after_picking_a_history_entry_keeps_the_find_text(int pickedIndex)
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        html.SetText("<html><body><p>w pociąg</p><p>że sobowtór</p><p>Dotykałem ich</p><p>w pociąg</p></body></html>");
        SettingsStore settings = new(Path.Combine(temp.Path, "settings.json"));
        SpellChecker spellChecker = new(settings, temp.Combine("dicts"), temp.Combine("user_dicts"));
        CodeTabViewModel tab = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        tab.Document.Text = html.GetText();

        FindReplaceViewModel vm = new(settings, new StatusBarService(), () => tab, Mock.Of<IMultiFileSearchHost>());
        string[] history = ["w pociąg", "że sobowtór", "Dotykałem ich"];
        foreach (string item in history)
        {
            vm.FindHistory.Add(item);
        }

        Window window = new() { Width = 900, Height = 400, Content = new FindReplaceView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "FindBox");

        // Picking from the dropped-down history: the ▾ button, selecting an entry in the list, closing the list.
        box.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FindBoxHistoryButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Popup popup = box.GetVisualDescendants().OfType<Popup>().Single();
        ListBox list = popup.Child!.GetVisualDescendants().Prepend(popup.Child).OfType<ListBox>().Single();
        list.SelectedItem = history[pickedIndex];
        Dispatcher.UIThread.RunJobs();
        box.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        vm.FindText.Should().Be(history[pickedIndex]);

        for (int i = 0; i < 3; i++)
        {
            vm.FindNext().Should().BeTrue(vm.Message);
            Dispatcher.UIThread.RunJobs();

            vm.FindText.Should().Be(history[pickedIndex], $"Find Next #{i + 1} does not clear the field");
            box.Text.Should().Be(history[pickedIndex]);
        }

        vm.FindHistory[0].Should().Be(history[pickedIndex]);
        vm.FindHistory.Should().HaveCount(3);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("w pociąg", false)]
    [InlineData("Dotykałem ich", false)]
    [InlineData("nowy tekst", false)]
    [InlineData("w pociąg", true)]
    [InlineData("Dotykałem ich", true)]
    [InlineData("nowy tekst", true)]
    public void Find_next_after_typing_keeps_the_find_text(string typed, bool openHistoryFirst)
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        html.SetText("<html><body><p>w pociąg nowy tekst</p><p>Dotykałem ich</p><p>w pociąg nowy tekst Dotykałem ich</p></body></html>");
        SettingsStore settings = new(Path.Combine(temp.Path, "settings.json"));
        SpellChecker spellChecker = new(settings, temp.Combine("dicts"), temp.Combine("user_dicts"));
        CodeTabViewModel tab = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        tab.Document.Text = html.GetText();

        FindReplaceViewModel vm = new(settings, new StatusBarService(), () => tab, Mock.Of<IMultiFileSearchHost>());
        foreach (string item in new[] { "w pociąg", "że sobowtór", "Dotykałem ich" })
        {
            vm.FindHistory.Add(item);
        }

        var view = new FindReplaceView { DataContext = vm };
        Window window = new() { Width = 900, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "FindBox");
        if (openHistoryFirst)
        {
            box.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FindBoxHistoryButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            box.IsDropDownOpen = false;
        }

        box.Focus();
        window.KeyTextInput(typed);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        vm.FindText.Should().Be(typed);

        Button findNext = window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.FindNextCommand));
        for (int i = 0; i < 3; i++)
        {
            findNext.Focus();
            findNext.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            box.Text.Should().Be(typed, $"Find Next #{i + 1} does not clear the field");
            vm.FindText.Should().Be(typed);
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("w pociąg")]
    [InlineData("nowy tekst")]
    public void Multi_file_find_next_keeps_the_find_text(string typed)
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        html.SetText("<html><body><p>w pociąg nowy tekst</p><p>w pociąg nowy tekst</p></body></html>");
        SettingsStore settings = new(Path.Combine(temp.Path, "settings.json"));
        SpellChecker spellChecker = new(settings, temp.Combine("dicts"), temp.Combine("user_dicts"));
        CodeTabViewModel tab = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        tab.Document.Text = html.GetText();
        tab.UpdateSelection(0, 0);
        tab.UpdateCaret(1, 1, 0, -1);

        var host = new Mock<IMultiFileSearchHost>();
        host.SetupGet(h => h.HasBook).Returns(true);
        host.Setup(h => h.ResolveLookWhere(It.IsAny<LookWhere>())).Returns(new TextResource[] { html });
        host.Setup(h => h.FindOpenTab(It.IsAny<TextResource>())).Returns(tab);
        host.Setup(h => h.OpenResourceAtMatch(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<string, int, int>((_, s, e) =>
            {
                tab.UpdateSelection(s, e);
                tab.UpdateCaret(1, 1, e, -1);
            });
        FindReplaceViewModel vm = new(settings, new StatusBarService(), () => tab, host.Object)
        {
            LookWhereIndex = (int)LookWhere.AllHtmlFiles,
            OptionWrap = false,
        };
        vm.FindHistory.Add("w pociąg");
        vm.FindHistory.Add("inne");

        Window window = new() { Width = 900, Height = 400, Content = new FindReplaceView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "FindBox");
        box.Focus();
        window.KeyTextInput(typed);
        Dispatcher.UIThread.RunJobs();

        // Two occurrences in the only file: two matches, then the search ends (Wrap is off, so multi-file
        // Find Next stops at the end of the scope); the field must not be cleared in any of these cases.
        for (int i = 0; i < 3; i++)
        {
            bool expectFound = i < 2;
            vm.FindNext().Should().Be(expectFound, vm.Message);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            box.Text.Should().Be(typed, $"Find Next #{i + 1} does not clear the field");
            vm.FindText.Should().Be(typed);
        }

        window.Close();
    }
}
