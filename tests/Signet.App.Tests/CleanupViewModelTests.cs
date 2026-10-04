using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="CleanupViewModel"/> — the sections of the Cleanup dialog and re-planning after every toggle.
/// </summary>
public sealed class CleanupViewModelTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly Book _book;

    public CleanupViewModelTests()
    {
        // A book with an unused selector whose rule is the only user of an image.
        string tree = _temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Media, tree);
        string opfPath = Path.Combine(tree, "EPUB", "package.opf");
        File.WriteAllText(
            opfPath,
            File.ReadAllText(opfPath).Replace(
                "  </manifest>",
                "    <item id=\"bg\" href=\"images/bg.png\" media-type=\"image/png\"/>\n  </manifest>",
                StringComparison.Ordinal));
        File.Copy(Path.Combine(tree, "EPUB", "images", "cover.png"), Path.Combine(tree, "EPUB", "images", "bg.png"));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.ghost { background: url(../images/bg.png); }\n");
        string epub = EpubBuilder.BuildInto(tree, _temp, "book.epub");
        _book = new ImportEpub(epub).GetBook();
    }

    public void Dispose()
    {
        _book.Dispose();
        _temp.Dispose();
    }

    private CleanupViewModel New(params CleanupStep[] enabled) =>
        new(CleanupAnalysis.Prepare(_book).Analysis!, enabled, (_, _) => { }, ApplyToBook);

    private CleanupAnalysis? ApplyToBook(CleanupPlan plan, IReadOnlyList<CleanupStep> enabledSteps)
    {
        _book.ApplyCleanup(plan);
        return CleanupAnalysis.Prepare(_book).Analysis;
    }

    private static CleanupAnalysis? NoApply(CleanupPlan plan, IReadOnlyList<CleanupStep> enabledSteps) => null;

    private static CleanupSectionViewModel Section(CleanupViewModel vm, CleanupStep step) =>
        vm.Sections.Single(s => s.Step == step);

    private static CleanupTabViewModel Tab(CleanupViewModel vm, CleanupStep step) =>
        vm.Tabs.Single(t => t.Sections.Any(s => s.Step == step));

    [Fact]
    public void Sections_follow_the_execution_order_and_the_remembered_choice()
    {
        CleanupViewModel vm = New(CleanupStep.UnusedMedia);

        vm.Sections.Select(s => s.Step).Should().Equal(Enum.GetValues<CleanupStep>());
        vm.EnabledSteps.Should().Equal(CleanupStep.UnusedMedia);
        Section(vm, CleanupStep.MergeSameSelectors).HasWarning.Should().BeTrue();
        Section(vm, CleanupStep.UnusedMedia).HasWarning.Should().BeFalse();
        vm.Tabs[1].Sections.Should().OnlyContain(s => s.HasWarning, "every HTML step can have risky (⚠) items");
    }

    [Fact]
    public void The_steps_are_grouped_into_CSS_HTML_and_Files_tabs()
    {
        CleanupViewModel vm = New();

        vm.Tabs.Should().HaveCount(3);
        vm.Tabs[0].Sections.Select(s => s.Step).Should().Equal(CleanupViewModel.CssSteps);
        vm.Tabs[1].Sections.Select(s => s.Step).Should().Equal(CleanupViewModel.HtmlSteps);
        vm.Tabs[2].Sections.Select(s => s.Step).Should().Equal(CleanupViewModel.FilesSteps);
    }

    [Fact]
    public void Nothing_enabled_means_no_changes()
    {
        CleanupViewModel vm = New();

        vm.Tabs.Should().OnlyContain(t => !t.HasChanges && !t.CleanCommand.CanExecute(null));
        vm.Sections.Should().OnlyContain(s => s.Items.Count == 0);
    }

    [Fact]
    public void The_tabs_are_planned_independently()
    {
        CleanupViewModel vm = New(CleanupStep.UnusedMedia);

        Section(vm, CleanupStep.UnusedSelectors).IsEnabled = true;

        Section(vm, CleanupStep.UnusedSelectors).Items.Should().ContainSingle(i => i.Text == ".ghost");
        Section(vm, CleanupStep.UnusedMedia).Items.Should().BeEmpty();
        Tab(vm, CleanupStep.UnusedSelectors).HasChanges.Should().BeTrue();
        Tab(vm, CleanupStep.UnusedMedia).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Unchecking_an_item_replans_and_keeps_the_row()
    {
        CleanupViewModel vm = New(CleanupStep.UnusedSelectors);
        CleanupItemViewModel ghost = Section(vm, CleanupStep.UnusedSelectors).Items.Single(i => i.Text == ".ghost");

        ghost.IsChecked = false;

        Section(vm, CleanupStep.UnusedSelectors).Items.Should().Contain(ghost);
        Tab(vm, CleanupStep.UnusedSelectors).Plan.GetStep(CleanupStep.UnusedSelectors)!.AppliedCount.Should().Be(0);
    }

    [Fact]
    public void Cleaning_a_tab_applies_only_its_plan_and_replans_the_other_tabs_on_the_changed_book()
    {
        CleanupViewModel vm = New(CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia);
        Section(vm, CleanupStep.UnusedMedia).Items.Should().BeEmpty();

        Tab(vm, CleanupStep.UnusedSelectors).CleanCommand.Execute(null);

        _book.GetCssResources().Single().GetText().Should().NotContain(".ghost");
        _book.GetMediaResources().Should().Contain(r => r.BookPath.EndsWith("bg.png", StringComparison.Ordinal));
        Section(vm, CleanupStep.UnusedSelectors).Items.Should().NotContain(i => i.Text == ".ghost");
        Tab(vm, CleanupStep.UnusedSelectors).HasChanges.Should().BeFalse();
        Section(vm, CleanupStep.UnusedMedia).Items.Should().ContainSingle(i => i.BookPath.EndsWith("bg.png", StringComparison.Ordinal));
        Tab(vm, CleanupStep.UnusedMedia).HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Cleaning_requests_closing_when_the_book_cannot_be_reanalysed()
    {
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(_book).Analysis!, new[] { CleanupStep.UnusedSelectors }, (_, _) => { }, NoApply);
        bool closeRequested = false;
        vm.CloseRequested += (_, _) => closeRequested = true;

        Tab(vm, CleanupStep.UnusedSelectors).CleanCommand.Execute(null);

        closeRequested.Should().BeTrue();
    }

    [Fact]
    public void A_risky_merge_is_unchecked_until_the_user_checks_it()
    {
        using TempDir temp = new();
        using Book book = RiskyMergeBook(temp);
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, new[] { CleanupStep.MergeSameSelectors }, (_, _) => { }, NoApply);
        CleanupSectionViewModel section = Section(vm, CleanupStep.MergeSameSelectors);
        CleanupItemViewModel merge = section.Items.Single();

        merge.IsRisky.Should().BeTrue();
        merge.IsChecked.Should().BeFalse();
        merge.Consequences.Should().NotBeEmpty();
        Tab(vm, CleanupStep.MergeSameSelectors).HasChanges.Should().BeFalse();

        merge.IsChecked = true;

        Tab(vm, CleanupStep.MergeSameSelectors).HasChanges.Should().BeTrue();
        Tab(vm, CleanupStep.MergeSameSelectors).Plan.GetStep(CleanupStep.MergeSameSelectors)!.Items.Single().IsApplied.Should().BeTrue();
    }

    [Fact]
    public void Select_all_does_not_accept_a_risky_merge()
    {
        using TempDir temp = new();
        using Book book = RiskyMergeBook(temp);
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, new[] { CleanupStep.MergeSameSelectors }, (_, _) => { }, NoApply);
        CleanupSectionViewModel section = Section(vm, CleanupStep.MergeSameSelectors);

        section.SelectAllCommand.Execute(null);

        section.Items.Single().IsChecked.Should().BeFalse();
        Tab(vm, CleanupStep.MergeSameSelectors).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Select_risky_accepts_risky_merges_and_is_offered_only_when_there_are_some()
    {
        using TempDir temp = new();
        using Book book = RiskyMergeBook(temp);
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, new[] { CleanupStep.MergeSameSelectors }, (_, _) => { }, NoApply);
        CleanupSectionViewModel section = Section(vm, CleanupStep.MergeSameSelectors);
        section.HasRiskyItems.Should().BeTrue();
        Section(vm, CleanupStep.UnusedSelectors).HasRiskyItems.Should().BeFalse();

        section.SelectRiskyCommand.Execute(null);

        section.Items.Single().IsChecked.Should().BeTrue();
        Tab(vm, CleanupStep.MergeSameSelectors).HasChanges.Should().BeTrue();
    }

    private static Book RiskyMergeBook(TempDir temp)
    {
        string tree = temp.Combine("risky");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.a { color: red; }\n.b { color: green; background: white; }\n.a { background: blue; }\n");
        string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapter, File.ReadAllText(chapter).Replace("<p>Hello, world.</p>", "<p class=\"a b\">Hello, world.</p>", StringComparison.Ordinal));
        return new ImportEpub(EpubBuilder.BuildInto(tree, temp, "risky.epub")).GetBook();
    }

    [Fact]
    public void Select_none_unchecks_every_item_of_the_section()
    {
        CleanupViewModel vm = New(CleanupStep.UnusedSelectors);
        CleanupSectionViewModel section = Section(vm, CleanupStep.UnusedSelectors);

        section.SelectNoneCommand.Execute(null);

        section.Items.Should().OnlyContain(i => !i.IsChecked);
        Tab(vm, CleanupStep.UnusedSelectors).HasChanges.Should().BeFalse();
    }
}
