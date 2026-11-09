using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The Find and Replace fields take several lines (Ctrl+Enter), grow with them and use the font of Preferences.</summary>
public sealed class FindReplaceMultilineTests
{
    private static (Window Window, FindReplaceViewModel Vm, AutoCompleteBox Box) Open(TempDir temp)
    {
        FindReplaceViewModel vm = new(
            new SettingsStore(Path.Combine(temp.Path, "settings.xml")),
            new StatusBarService(),
            () => (CodeTabViewModel?)null,
            Mock.Of<IMultiFileSearchHost>());
        Window window = new() { Width = 900, Height = 400, Content = new FindReplaceView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return (window, vm, window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "FindBox"));
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void A_search_of_several_lines_is_shown_in_full_and_the_field_grows()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, FindReplaceViewModel vm, AutoCompleteBox box) = Open(temp);
        TextBox text = box.GetVisualDescendants().OfType<TextBox>().First();
        vm.FindText = "</p>";
        Render(window);
        double oneLine = text.Bounds.Height;

        // Act
        vm.FindText = "</p>\n\n<p class=\"calibre1\">";
        Render(window);

        // Assert
        text.AcceptsReturn.Should().BeTrue();
        text.Text.Should().Be("</p>\n\n<p class=\"calibre1\">");
        text.Bounds.Height.Should().BeGreaterThan(oneLine, "three lines need more room than one");
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_Enter_adds_a_line_to_the_field()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, FindReplaceViewModel vm, AutoCompleteBox box) = Open(temp);
        TextBox text = box.GetVisualDescendants().OfType<TextBox>().First();
        vm.FindText = "ab";
        Render(window);
        text.Focus();
        text.CaretIndex = 1;

        // Act
        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        Render(window);

        // Assert
        vm.FindText.Should().Be("a\nb");
        window.Close();
    }

    [AvaloniaFact]
    public void The_fields_use_the_font_of_preferences()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, FindReplaceViewModel vm, AutoCompleteBox box) = Open(temp);

        // Act
        vm.FieldFont = new FindReplaceAppearance("Courier New", 19, 0);
        Render(window);

        // Assert
        box.FontFamily.Name.Should().Be("Courier New");
        box.FontSize.Should().Be(19);
        window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "ReplaceBox").FontSize.Should().Be(19);
        window.Close();
    }

    [AvaloniaFact]
    public void The_selected_text_of_the_fields_is_painted_in_the_color_of_the_theme()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, _, AutoCompleteBox box) = Open(temp);
        TextBox text = box.GetVisualDescendants().OfType<TextBox>().First();

        // Act
        Application.Current!.TryGetResource("SignetFindReplaceSelectionBrush", null, out object? themed);

        // Assert
        themed.Should().BeAssignableTo<IBrush>("the theme defines the color of the selection of the fields");
        text.SelectionBrush.Should().BeSameAs(themed);
        window.Close();
    }

    [AvaloniaFact]
    public void The_history_list_that_drops_down_uses_the_font_of_the_fields()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, FindReplaceViewModel vm, AutoCompleteBox box) = Open(temp);
        vm.FindHistory.Add("one");
        vm.FindHistory.Add("two");

        // Act
        vm.FieldFont = new FindReplaceAppearance("Courier New", 19, 0);
        box.IsDropDownOpen = true;
        Render(window);
        Avalonia.Controls.Primitives.Popup popup = box.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First();
        Avalonia.Controls.Primitives.SelectingItemsControl list = ((Avalonia.LogicalTree.ILogical)popup.Child!).GetSelfAndLogicalDescendants()
            .OfType<Avalonia.Controls.Primitives.SelectingItemsControl>().First();

        // Assert
        list.FontFamily.Name.Should().Be("Courier New");
        list.FontSize.Should().Be(19);
        window.Close();
    }

    [AvaloniaFact]
    public void With_the_text_below_the_icon_the_content_of_the_action_buttons_is_centered()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, FindReplaceViewModel vm, _) = Open(temp);
        Button find = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("findAction") && (string?)b.Tag == "find");

        // Act & Assert
        find.HorizontalContentAlignment.Should().Be(Avalonia.Layout.HorizontalAlignment.Left, "the icons stand in one column");

        // Act
        vm.ActionButtonDisplay = FindActionButtonDisplay.TextBelowIcon;
        Render(window);

        // Assert
        find.HorizontalContentAlignment.Should().Be(Avalonia.Layout.HorizontalAlignment.Center);
        window.Close();
    }
}
