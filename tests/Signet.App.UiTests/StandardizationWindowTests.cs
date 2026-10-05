using System.Collections.Generic;
using System.Linq;
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
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Standardize EPUB" window.</summary>
public sealed class StandardizationWindowTests
{
    [AvaloniaFact]
    public void Window_shows_a_checkbox_and_an_info_icon_with_the_detailed_description_for_every_step()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp)).GetBook();
        StandardizationViewModel vm = new(EpubStandardization.AllSteps, steps => EpubStandardization.Plan(book, steps), _ => { });
        StandardizationWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        List<CheckBox> checkBoxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();
        checkBoxes.Select(c => c.DataContext).Should().Equal(vm.Sections);
        checkBoxes.Should().OnlyContain(c => c.IsChecked == true);

        List<Border> infoIcons = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("info")).ToList();
        infoIcons.Should().HaveCount(vm.Sections.Count);
        foreach (Border icon in infoIcons)
        {
            ToolTip.SetIsOpen(icon, true);
            Dispatcher.UIThread.RunJobs();

            TextBlock tip = ToolTip.GetTip(icon).Should().BeOfType<TextBlock>().Subject;
            tip.Text.Should().Be(((StandardizationSectionViewModel)icon.DataContext!).Info);
            tip.IsEffectivelyVisible.Should().BeTrue();
            tip.Bounds.Width.Should().BeGreaterThan(400, "the long description gets a wider tooltip than the Fluent default");
            ToolTip.SetIsOpen(icon, false);
        }

        Button apply = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyButton");
        apply.Content.Should().Be(Strings.Get("StandardizeWindow_Apply"));
        apply.IsEffectivelyEnabled.Should().BeTrue("the deep-folders book is not in the standard layout");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton")
            .Content.Should().Be(Strings.Get("Common_Close"));

        window.Close();
    }
}
