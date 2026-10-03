using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Diff;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the Compare window.</summary>
public sealed class DiffWindowTests
{
    private static DiffViewModel Model(TempDir temp)
    {
        string left = temp.Combine("left.xhtml");
        string right = temp.Combine("right.xhtml");
        File.WriteAllText(left, "<p>Ala ma kota</p>\n<p>bez zmian</p>");
        File.WriteAllText(right, "<p>Ala ma psa</p>\n<p>bez zmian</p>\n<p>nowy</p>");
        BookFileDiff diff = new(BookFileChange.Modified, "EPUB/ch.xhtml", "EPUB/ch.xhtml", left, right, BookFileContentKind.Text);
        return new DiffViewModel(new[] { diff }, "Start", "Current");
    }

    [AvaloniaFact]
    public void Side_by_side_view_renders_both_sides_with_word_level_highlights()
    {
        using TempDir temp = new();
        DiffWindow window = new() { DataContext = Model(temp) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        DiffLineText[] lines = window.GetVisualDescendants().OfType<DiffLineText>().Where(l => l.IsEffectivelyVisible).ToArray();
        lines.Should().NotBeEmpty();
        lines.Should().Contain(l => l.IsLeft && l.Kind == DiffLineKind.Modified);
        DiffLineText modifiedRight = lines.First(l => !l.IsLeft && l.Kind == DiffLineKind.Modified);
        modifiedRight.Inlines!.OfType<Run>().Should().Contain(r => r.Background != null && r.Text!.Contains("psa"));
        modifiedRight.Background.Should().NotBeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Switching_to_unified_view_shows_the_single_column_list()
    {
        using TempDir temp = new();
        DiffViewModel vm = Model(temp);
        DiffWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.ViewIndex = 1;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        ListBox unified = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UnifiedList");
        ListBox sideBySide = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SideBySideList");
        unified.IsEffectivelyVisible.Should().BeTrue();
        sideBySide.IsEffectivelyVisible.Should().BeFalse();
        window.GetVisualDescendants().OfType<DiffLineText>().Where(l => l.IsEffectivelyVisible)
            .Should().Contain(l => l.Kind == DiffLineKind.Inserted);
        window.Close();
    }
}
