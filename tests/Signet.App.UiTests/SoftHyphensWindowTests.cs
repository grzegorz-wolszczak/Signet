using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Rendering of the "Hyphenation" dialog: the tree of its side with the columns of that side, the switch between adding
/// and removing, and what a side with nothing to do says.
/// </summary>
public sealed class SoftHyphensWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_dialog_shows_the_columns_of_its_side_and_the_switch_changes_them()
    {
        // Arrange
        TempDir patterns = _disposables.Add(new TempDir("hyph-ui-"));
        File.WriteAllText(patterns.Combine("hyph_pl_PL.dic"), TestHyphenation.BeforeEveryCPatterns);
        SoftHyphenSurvey survey = SoftHyphenSurvey.Of(
            [("Text/a.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"pl\"><head><title>T</title></head><body><p>cierpliwości</p></body></html>")],
            string.Empty);
        SoftHyphensViewModel vm = new(
            survey, () => new HyphenationPatternFiles(patterns.Path), (_, _) => { },
            _ => Task.FromResult<SoftHyphenSurvey?>(null), _ => Task.FromResult<SoftHyphenSurvey?>(null),
            _ => Task.FromResult<SoftHyphenSurvey?>(null), SoftHyphenForm.Character, _ => { });
        SoftHyphensWindow window = SoftHyphensWindow.Create(vm);

        // Act
        window.Show();
        Settle(window);
        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "Tree");

        // Assert
        tree.Rows!.Count.Should().Be(1, "Polish, collapsed");
        tree.Columns!.Count.Should().Be(4, "name, patterns, source, words");
        tree.IsEffectivelyVisible.Should().BeTrue();

        // Act
        vm.IsRemoveMode = true;
        Settle(window);

        // Assert
        tree.Columns!.Count.Should().Be(4, "name, source, soft hyphens, context");
        tree.IsEffectivelyVisible.Should().BeFalse("the book has no soft hyphen to remove");
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EmptyText").IsEffectivelyVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "MinWordLengthBox").IsEffectivelyVisible
            .Should().BeFalse("the shortest word is about adding");
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "FormPanel").IsEffectivelyVisible
            .Should().BeFalse("removing writes no soft hyphen");

        // Act
        vm.IsConvertMode = true;
        Settle(window);

        // Assert
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "FormPanel").IsEffectivelyVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "CharacterFormButton").IsChecked.Should().BeTrue();

        window.Close();
    }
}
