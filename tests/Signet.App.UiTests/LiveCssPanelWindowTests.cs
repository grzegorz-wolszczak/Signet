using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Parsers;

namespace Signet.App.UiTests;

/// <summary>
/// Rendering of the "Live CSS Panel" window: the levels set apart by a line and foldable away, the check box that
/// lists what the element inherits, and the CSS shown in the colors of Code Editor.
/// </summary>
public sealed class LiveCssPanelWindowTests
{
    private const string CssPath = "EPUB/styles/style.css";
    private const string CssText = ".a { color: red; }\n.b { color: blue; }";

    private static CssCascadeResult Result() =>
        new(
            "EPUB/text/chapter1.xhtml",
            0,
            "p.b",
            new[]
            {
                new CssCascadeLevel("div.a", 0, false, new[]
                {
                    new CssMatchedRule(CssPath, 0, ".a", new CssSpecificity(0, 0, 1, 0), false,
                        new[] { new CssMatchedDeclaration("color", "red", false, false) }),
                }),
                new CssCascadeLevel("p.b", 10, true, new[]
                {
                    new CssMatchedRule(CssPath, CssText.IndexOf(".b", System.StringComparison.Ordinal), ".b",
                        new CssSpecificity(0, 0, 1, 0), false,
                        new[] { new CssMatchedDeclaration("margin-top", "18px", false, false) }),
                }),
            })
        {
            Inherited = new[]
            {
                new CssInheritedDeclaration("color", "red", false, "div.a", new CssDeclarationSource(CssPath, 0, ".a", false)),
            },
        };

    private static (LiveCssPanelWindow Window, LiveCssPanelViewModel Vm) Open()
    {
        LiveCssPanelViewModel vm = new();
        vm.Update(Result(), _ => CssText, (_, _) => { });
        LiveCssPanelWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return (window, vm);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Every_level_but_the_first_is_set_off_by_a_line_and_only_the_ancestors_can_be_folded_away()
    {
        // Arrange
        (LiveCssPanelWindow window, LiveCssPanelViewModel vm) = Open();

        // Act
        ToggleButton[] toggles = window.GetVisualDescendants().OfType<ToggleButton>()
            .Where(t => t.Classes.Contains("levelToggle")).ToArray();

        // Assert
        vm.Levels.Should().HaveCount(2);
        toggles.Should().HaveCount(2);
        toggles[0].IsEffectivelyVisible.Should().BeTrue("the ancestor can be folded away");
        toggles[1].IsEffectivelyVisible.Should().BeFalse("the element under the caret is what the panel is about");

        // Act — the triangle folds the level away.
        toggles[0].IsChecked = false;
        Settle(window);

        // Assert
        vm.Levels[0].IsExpanded.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void The_check_box_under_the_last_header_shows_what_the_element_inherits()
    {
        // Arrange
        (LiveCssPanelWindow window, LiveCssPanelViewModel vm) = Open();
        CheckBox box = window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => Equals(c.Content, Strings.Get("LiveCssPanelWindow_ShowInherited")) && c.IsEffectivelyVisible);

        // Assert
        vm.ShowInherited.Should().BeFalse();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().NotContain(t => t.Text == Strings.Get("LiveCssPanelWindow_InheritedHeader") && t.IsEffectivelyVisible);

        // Act
        box.IsChecked = true;
        Settle(window);

        // Assert
        vm.ShowInherited.Should().BeTrue();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("LiveCssPanelWindow_InheritedHeader") && t.IsEffectivelyVisible);
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Format("LiveCssPanelWindow_InheritedFrom", "div.a — style.css:1") && t.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_css_is_colored_the_way_Code_Editor_colors_it()
    {
        // Arrange
        (LiveCssPanelWindow window, _) = Open();

        // Act
        CssSyntaxText[] texts = window.GetVisualDescendants().OfType<CssSyntaxText>().ToArray();

        // Assert
        texts.Should().NotBeEmpty();
        CssSyntaxText declaration = texts.Single(t => t.CssText == "margin-top: 18px;");
        Run[] runs = declaration.Inlines!.OfType<Run>().ToArray();
        runs.Should().HaveCountGreaterThan(1, "the property and the value are colored apart");
        runs.Select(r => r.Text).Should().OnlyContain(t => t!.Length > 0);
        string.Concat(runs.Select(r => r.Text)).Should().Be("margin-top: 18px;");
        runs.Select(r => r.Foreground).Distinct().Should().HaveCountGreaterThan(1);

        texts.Should().Contain(t => t.CssText == ".a" && t.IsSelector);
        window.Close();
    }
}
