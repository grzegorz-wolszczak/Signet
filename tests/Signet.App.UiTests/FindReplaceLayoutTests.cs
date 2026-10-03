using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Layout of the Find &amp; Replace panel: buttons with icons and the options row.</summary>
public sealed class FindReplaceLayoutTests
{
    private static (Window Window, FindReplaceView View, FindReplaceViewModel Vm) Show(TempDir temp)
    {
        SettingsStore settings = new(Path.Combine(temp.Path, "settings.json"));
        new IconThemeManager(settings, NullLogger<IconThemeManager>.Instance).ApplySaved();
        FindReplaceViewModel vm = new(settings, new StatusBarService(), () => (CodeTabViewModel?)null, Mock.Of<IMultiFileSearchHost>());
        FindReplaceView view = new() { DataContext = vm };
        Window window = new() { Width = 1400, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return (window, view, vm);
    }

    [AvaloniaFact]
    public void Six_action_buttons_with_icons_are_laid_out()
    {
        using TempDir temp = new();
        (Window window, FindReplaceView view, FindReplaceViewModel vm) = Show(temp);

        Grid grid = view.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ActionButtons");
        Button[] buttons = grid.Children.OfType<Button>()
            .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn).ToArray();

        buttons.Select(b => b.Tag).Should().Equal("find", "replace", "restart", "replace-find", "replace-all", "count-all");
        buttons.Select(b => b.Command).Should().Equal(
            vm.FindNextCommand, vm.ReplaceCommand, vm.RestartCommand,
            vm.ReplaceFindCommand, vm.ReplaceAllCommand, vm.CountCommand);

        foreach (Button button in buttons)
        {
            Image icon = button.GetVisualDescendants().OfType<Image>().Single();
            icon.Source.Should().NotBeNull($"button {button.Tag} has an icon from the current set");
            TextBlock text = button.GetVisualDescendants().OfType<TextBlock>().Single();
            icon.TranslatePoint(default, button)!.Value.X.Should().BeLessThan(
                text.TranslatePoint(default, button)!.Value.X, "the icon is to the left of the text");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Text_only_and_regex_options_are_available_outside_regex_mode()
    {
        using TempDir temp = new();
        (Window window, FindReplaceView view, FindReplaceViewModel vm) = Show(temp);
        vm.IsRegexMode.Should().BeFalse();

        CheckBox textOnly = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "TextOnlyCheckBox");
        textOnly.IsEffectivelyVisible.Should().BeTrue("the option also works in Normal mode, so it must be visible");
        textOnly.IsChecked = true;
        vm.RegexTextOnly.Should().BeTrue();

        DropDownButton regexOptions = view.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "RegexOptionsButton");
        regexOptions.IsEffectivelyVisible.Should().BeTrue();
        MenuItem[] items = ((MenuFlyout)regexOptions.Flyout!).Items.Cast<MenuItem>().ToArray();
        items.Should().HaveCount(4);
        items.Should().OnlyContain(i => i.ToggleType == MenuItemToggleType.CheckBox);

        window.Close();
    }
}
