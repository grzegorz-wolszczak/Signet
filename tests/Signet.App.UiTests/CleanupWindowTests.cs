using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Cleanup" window.</summary>
public sealed class CleanupWindowTests
{
    [AvaloniaFact]
    public void Window_shows_the_CSS_HTML_and_Files_tabs_each_with_its_own_Clean_button_and_a_global_Close()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook();
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, [], (_, _) => { }, (_, _) => System.Threading.Tasks.Task.FromResult<CleanupAnalysis?>(null));
        CleanupWindow window = new() { DataContext = vm };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.GetVisualDescendants().OfType<TabItem>().Select(t => t.GetVisualDescendants().OfType<TextBlock>().First().Text)

            // Assert
            .Should().Equal(Strings.Get("CleanupWindow_Tab_Css"), Strings.Get("CleanupWindow_Tab_Html"), Strings.Get("CleanupWindow_Tab_Files"));
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton")
            .Content.Should().Be(Strings.Get("Common_Close"));

        for (int i = 0; i < vm.Tabs.Count; i++)
        {
            // Act
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            Button clean = tabs.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, Strings.Get("CleanupWindow_Clean")) && b.IsEffectivelyVisible);

            // Assert
            clean.DataContext.Should().BeSameAs(vm.Tabs[i]);
            clean.IsEffectivelyEnabled.Should().BeFalse();
        }

        window.Close();
    }

    /// <summary>
    /// The description of a step is folded away until its triangle is clicked: a tab holds a dozen steps and their
    /// descriptions are long, so unfolded they push the summaries off the screen.
    /// </summary>
    [AvaloniaFact]
    public void The_description_of_a_step_is_folded_away_until_its_triangle_is_clicked()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook();
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, [], (_, _) => { }, (_, _) => System.Threading.Tasks.Task.FromResult<CleanupAnalysis?>(null));
        CleanupWindow window = new() { DataContext = vm };
        CleanupSectionViewModel section = vm.Tabs[0].Sections[0];

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        ToggleButton toggle = window.GetVisualDescendants().OfType<ToggleButton>()
            .First(t => t.Classes.Contains("descriptionToggle"));

        // Assert
        section.IsDescriptionExpanded.Should().BeFalse("descriptions start folded away");
        section.DescriptionGlyph.Should().Be("▸");
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().NotContain(t => t.Text == section.Description && t.IsEffectivelyVisible);

        // Act
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        section.IsDescriptionExpanded.Should().BeTrue();
        section.DescriptionGlyph.Should().Be("▾");
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == section.Description && t.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Undefined_class_details_show_one_tree_with_the_files_for_each_class()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = MultiFileUndefinedClassBook(temp);
        CleanupViewModel vm = new(
            CleanupAnalysis.Prepare(book).Analysis!,
            new[] { CleanupStep.UndefinedClasses },
            (_, _) => { },
            (_, _) => System.Threading.Tasks.Task.FromResult<CleanupAnalysis?>(null));
        CleanupSectionViewModel section = vm.Sections.Single(s => s.Step == CleanupStep.UndefinedClasses);
        section.IsExpanded = true;
        CleanupWindow window = new() { DataContext = vm };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(t => t.Source is not null);
        tree.IsEffectivelyVisible.Should().BeTrue();
        section.ClassGroups.Should().ContainSingle().Which.Children.Should().HaveCount(2);

        window.Close();
    }

    private static Book MultiFileUndefinedClassBook(TempDir temp)
    {
        string tree = temp.Combine("undefined-classes");
        TestFs.CopyDirectory(CorpusPaths.Epub3WithNcx, tree);
        string chapter1 = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(
            chapter1,
            File.ReadAllText(chapter1).Replace(
                "<p>First chapter.</p>",
                "<p class=\"cleanup-orphan\">First chapter.</p>",
                StringComparison.Ordinal));
        string chapter2 = Path.Combine(tree, "EPUB", "text", "chapter2.xhtml");
        File.WriteAllText(
            chapter2,
            File.ReadAllText(chapter2).Replace(
                "<p>Second chapter.</p>",
                "<p class=\"cleanup-orphan\">Second chapter.</p>",
                StringComparison.Ordinal));
        return new ImportEpub(EpubBuilder.BuildInto(tree, temp, "undefined-classes.epub")).GetBook();
    }
}
