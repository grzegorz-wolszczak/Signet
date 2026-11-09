using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>The "Accessibility" dialog: its three tabs and "Save".</summary>
public sealed class AccessibilityWindowTests
{
    private static Book NewBook(string version, string body)
    {
        Book book = BookCreator.CreateNewBook(version);
        book.GetHtmlResourcesExcludingNav()[0].SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
            + body + "\n</body>\n</html>");
        return book;
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static (Window Owner, AccessibilityWindow Window, Task Shown) Show(AccessibilityViewModel vm)
    {
        Window owner = new();
        owner.Show();
        Task shown = new AccessibilityWindow { DataContext = vm }.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        AccessibilityWindow window = owner.OwnedWindows.OfType<AccessibilityWindow>().Single();
        window.CaptureRenderedFrame();
        return (owner, window, shown);
    }

    [AvaloniaFact]
    public void The_tabs_show_the_languages_the_images_and_the_metadata_and_save_writes_and_closes()
    {
        // Arrange
        using Book book = NewBook("3.0", "<img src=\"a.png\"/><img src=\"b.png\" alt=\"B\"/>");
        AccessibilityLanguage.SetLanguages(book, ["pl", "en"]);
        List<AccessibilitySaveRequest> saved = new();
        using AccessibilityViewModel vm = new(book, (_, _) => { }, request =>
        {
            saved.Add(request);
            return Task.FromResult(true);
        });

        // Act
        (Window owner, AccessibilityWindow window, Task shown) = Show(vm);
        TabControl tabs = Named<TabControl>(window, "Tabs");

        // Assert
        tabs.Items.Count.Should().Be(3);
        window.Title.Should().Be(Strings.Get("AccessibilityWindow_Title"));
        Named<ListBox>(window, "LanguageList").ItemCount.Should().Be(2);
        (vm.BuildRequest().Languages, vm.BuildRequest().HtmlLanguage, vm.BuildRequest().ImageEdits.Count, vm.BuildRequest().Metadata).Should().Be((null, HtmlLanguageChoice.Keep, 0, null));
        Named<Button>(window, "SaveButton").IsEffectivelyEnabled.Should().BeFalse("nothing changed yet");

        // Act
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        Named<ListBox>(window, "ImageList").ItemCount.Should().Be(2);

        // Act
        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Named<TextBox>(window, "SummaryBox").Text = "Mostly text.";
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Summary.Should().Be("Mostly text.");
        Button save = Named<Button>(window, "SaveButton");
        save.IsEffectivelyEnabled.Should().BeTrue();

        // Act
        save.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        saved.Should().ContainSingle().Which.Metadata!.Summary.Should().Be("Mostly text.");
        shown.IsCompleted.Should().BeTrue("the dialog closes after saving");
        owner.Close();
    }

    [AvaloniaFact]
    public void The_language_radio_buttons_follow_the_choice_and_the_filter_hides_described_images()
    {
        // Arrange
        using Book book = NewBook("3.0", "<img src=\"a.png\"/><img src=\"b.png\" alt=\"B\"/>");
        using AccessibilityViewModel vm = new(book, (_, _) => { }, _ => Task.FromResult(true));
        (Window owner, AccessibilityWindow window, _) = Show(vm);

        // Act
        Named<RadioButton>(window, "OnlyMissingRadio").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.HtmlLanguage.Should().Be(HtmlLanguageChoice.OnlyMissing);
        Named<RadioButton>(window, "KeepRadio").IsChecked.Should().BeFalse();

        // Act
        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Named<CheckBox>(window, "OnlyUndescribedBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // Assert
        Named<ListBox>(window, "ImageList").ItemCount.Should().Be(1);
        owner.Close();
    }

    [AvaloniaFact]
    public void An_epub2_book_shows_a_note_instead_of_the_metadata()
    {
        // Arrange
        using Book book = NewBook("2.0", "<p>x</p>");
        using AccessibilityViewModel vm = new(book, (_, _) => { }, _ => Task.FromResult(true));

        // Act
        (Window owner, AccessibilityWindow window, _) = Show(vm);
        Named<TabControl>(window, "Tabs").SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == Strings.Get("AccessibilityWindow_Epub2Note")).Should().ContainSingle()
            .Which.IsEffectivelyVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "SummaryBox").Should().OnlyContain(t => !t.IsEffectivelyVisible);
        owner.Close();
    }
}
