using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="StandardizationViewModel"/> — the sections of the "Standardize EPUB" dialog, re-planning after
/// every toggle and "Apply".
/// </summary>
public sealed class StandardizationViewModelTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly Book _book;
    private readonly List<IReadOnlyList<StandardizationStep>> _applied = new();

    public StandardizationViewModelTests()
    {
        // The deep-folders book with an XHTML chapter saved as .html, so every path step has something to do.
        string tree = _temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.EdgeDeepFolders, tree);
        string body = Path.Combine(tree, "content", "pages", "body");
        File.Move(Path.Combine(body, "ch01.xhtml"), Path.Combine(body, "ch01.html"));
        string opfPath = Path.Combine(tree, "content", "book.opf");
        File.WriteAllText(opfPath, File.ReadAllText(opfPath).Replace("pages/body/ch01.xhtml", "pages/body/ch01.html", StringComparison.Ordinal));
        string epub = EpubBuilder.BuildInto(tree, _temp, "book.epub");
        _book = new ImportEpub(epub).GetBook();
    }

    public void Dispose()
    {
        _book.Dispose();
        _temp.Dispose();
    }

    private StandardizationViewModel New(params StandardizationStep[] enabled) =>
        new(enabled, steps => EpubStandardization.Plan(_book, steps), _applied.Add);

    private static StandardizationSectionViewModel Section(StandardizationViewModel vm, StandardizationStep step) =>
        vm.Sections.Single(s => s.Step == step);

    [Fact]
    public void Sections_follow_the_step_order_and_the_remembered_choice()
    {
        StandardizationViewModel vm = New(StandardizationStep.RebaseManifestIds);

        vm.Sections.Select(s => s.Step).Should().Equal(EpubStandardization.AllSteps);
        vm.EnabledSteps.Should().Equal(StandardizationStep.RebaseManifestIds);
        vm.Sections.Should().OnlyContain(s => s.Title.Length > 0 && s.Description.Length > 0 && s.Info.Length > 0);
        Section(vm, StandardizationStep.StandardFolders).Items.Should().BeEmpty("an unchecked step plans nothing");
        Section(vm, StandardizationStep.StandardFolders).Summary.Should().BeEmpty();
        Section(vm, StandardizationStep.RebaseManifestIds).HasItems.Should().BeTrue();
    }

    [Fact]
    public void Checking_an_earlier_step_replans_the_later_ones()
    {
        using UiCultureScope culture = new("en");
        StandardizationViewModel vm = New(StandardizationStep.RebaseManifestIds);
        StandardizationSectionViewModel ids = Section(vm, StandardizationStep.RebaseManifestIds);
        ids.Items.Should().Contain(i => i.Change.Before == "ch01" && i.Change.After == "ch01_html");

        Section(vm, StandardizationStep.StandardFileExtensions).IsEnabled = true;

        StandardizationSectionViewModel extensions = Section(vm, StandardizationStep.StandardFileExtensions);
        extensions.Summary.Should().Be(
            Strings.Format("StandardizeStep_StandardFileExtensions_Summary", 1) + Strings.Format("StandardizeWindow_Summary_SideIds", 1));
        extensions.Items.Select(i => (i.Text, i.FileText)).Should().Equal(
            ("content/pages/body/ch01.html → content/pages/body/ch01.xhtml", string.Empty),
            ("ID: ch01 → ch01.xhtml", "content/pages/body/ch01.xhtml"));
        ids.Items.Should().Contain(i => i.Change.Before == "ch01.xhtml" && i.Change.After == "ch01_xhtml");
    }

    [Fact]
    public void Select_none_leaves_nothing_to_apply_and_select_all_checks_every_step()
    {
        StandardizationViewModel vm = New(StandardizationStep.StandardFolders);
        vm.HasChanges.Should().BeTrue();

        vm.SelectNoneCommand.Execute(null);

        vm.EnabledSteps.Should().BeEmpty();
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        vm.SelectAllCommand.Execute(null);

        vm.EnabledSteps.Should().Equal(EpubStandardization.AllSteps);
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Apply_runs_the_checked_steps_and_closes_the_dialog()
    {
        StandardizationViewModel vm = New(StandardizationStep.StandardFolders, StandardizationStep.ManifestMediaTypes);
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.ApplyCommand.Execute(null);

        _applied.Should().ContainSingle()
            .Which.Should().Equal(StandardizationStep.StandardFolders, StandardizationStep.ManifestMediaTypes);
        closed.Should().BeTrue();
    }
}
