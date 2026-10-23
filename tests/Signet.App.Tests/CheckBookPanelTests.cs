using System;
using Signet.App.Infrastructure;
using System.Threading;
using System.Collections.Generic;
using Signet.App.Tests.TestSupport;
using System.Threading.Tasks;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The Validation Results panel as the Check Book tool (calibre CF-01): automatic fixes with a checkpoint, skipped
/// types of problems kept in the settings, and the panel view model.
/// </summary>
public sealed class CheckBookPanelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private const string DummyFontCode = "Validation_FontCorrupt";

    private (MainWindowViewModel Vm, Book Book) Load(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());
        vm.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);
        return (vm, ((IBookWorkspace)vm).CurrentBook!);
    }

    [Fact]
    public async Task Fix_all_applies_the_fixes_after_a_checkpoint_and_checks_the_book_again()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            TempDir temp = _disposables.Add(new TempDir());

            // Arrange
            (MainWindowViewModel vm, Book book) = Load(temp);
            try
            {
                HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
                html.SetText(html.GetText().Replace("<body>", "<body>\nLoose text", StringComparison.Ordinal));

                // Act
                vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
                await vm.WhenIdleAsync();

                // Assert
                vm.ValidationResults.Rows.Should().Contain(r => r.Result.Code == "Validation_BareBodyText" && r.CanFix);
                vm.ValidationResults.HasFixable.Should().BeTrue();

                // Act
                vm.ValidationResults.FixAll();
                await vm.WhenIdleAsync();

                // Assert
                html.GetText().Should().Contain("<p>Loose text</p>");
                vm.ValidationResults.Rows.Should().NotContain(r => r.Result.Code == "Validation_BareBodyText");
                vm.CheckpointHistory.UndoMessage.Should().Be(Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_FixProblems")));
                book.Modified.Should().BeTrue();
            }
            finally
            {
                vm.CheckpointHistory.Dispose();
            }
        });
    }

    [Fact]
    public async Task A_skipped_type_of_problem_is_left_out_until_it_is_restored()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel vm, _) = Load(temp);
            try
            {
                vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
                await vm.WhenIdleAsync();
                ValidationResultRow font = vm.ValidationResults.Rows.Single(r => r.Result.Code == DummyFontCode);

                // Act
                vm.ValidationResults.SkipRule(font);

                // Assert
                vm.ValidationResults.Rows.Should().NotContain(r => r.Result.Code == DummyFontCode);
                vm.ValidationResults.SkippedRules.Select(r => r.Code).Should().Equal(DummyFontCode);
                vm.ValidationResults.HasSkippedRules.Should().BeTrue();

                // Act
                vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
                await vm.WhenIdleAsync();

                // Assert
                vm.ValidationResults.Rows.Should().NotContain(r => r.Result.Code == DummyFontCode, "the skip is remembered");

                // Act
                vm.ValidationResults.RestoreRules(new[] { DummyFontCode });
                await vm.WhenIdleAsync();

                // Assert
                vm.ValidationResults.Rows.Should().Contain(r => r.Result.Code == DummyFontCode);
                vm.ValidationResults.HasSkippedRules.Should().BeFalse();
            }
            finally
            {
                vm.CheckpointHistory.Dispose();
            }
        });
    }

    private sealed class Collecting : IProgress<WorkProgress>
    {
        public List<WorkProgress> Reports { get; } = new();

        public void Report(WorkProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task The_check_runs_under_the_progress_runner_with_the_name_of_the_action_and_reports_where_it_is()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel vm, _) = Load(temp);
            Collecting progress = new();
            string? title = null;
            bool? cancellable = null;
            vm.ProgressRunner = (t, work, c) =>
            {
                title = t;
                cancellable = c;
                return work(progress, CancellationToken.None);
            };

            // Act
            vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            await vm.WhenIdleAsync();

            // Assert
            title.Should().Be("Well-Formed Check EPUB");
            cancellable.Should().BeTrue("the check only reads the book, so nothing has to be undone");
            progress.Reports.Select(r => r.Message).Should().Contain(Strings.Format("ValidationProgress_Stage", Strings.Get("ValidationProgress_Stage_Footnotes")));
            progress.Reports.Select(r => r.Fraction).OfType<double>().Should().BeInAscendingOrder().And.OnlyContain(f => f >= 0 && f < 1);
            vm.ValidationResults.HasResults.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Cancelling_the_check_leaves_the_results_as_they_were_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel vm, _) = Load(temp);
            using CancellationTokenSource pressed = new();
            vm.ProgressRunner = (_, work, _) =>
            {
                pressed.Cancel();
                return work(new Collecting(), pressed.Token);
            };

            // Act
            vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            await vm.WhenIdleAsync();

            // Assert
            vm.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", "Well-Formed Check EPUB"));
            vm.ValidationResults.HasResults.Should().BeFalse("nothing was loaded into the panel");
        });
    }

    [Fact]
    public void Fix_selected_raises_only_the_results_that_have_a_fix()
    {
        // Arrange
        ValidationResultsViewModel vm = new();
        ValidationResult fixable = new(ValidationSeverity.Warning, "a.xhtml", -1, -1, "a", "Validation_BareBodyText",
            new BareBodyTextFix("a.xhtml"));
        ValidationResult plain = new(ValidationSeverity.Error, "b.xhtml", -1, -1, "b", "Validation_DeadLink");
        vm.LoadResults(new[] { fixable, plain });
        System.Collections.Generic.IReadOnlyList<ValidationResult>? requested = null;
        vm.FixRequested += (_, results) => requested = results;

        // Act
        vm.SetSelection(new[] { vm.Rows[1] });

        // Assert
        vm.CanFixSelection.Should().BeFalse();

        // Act
        vm.SetSelection(vm.Rows.ToList());

        // Assert
        vm.CanFixSelection.Should().BeTrue();

        // Act
        vm.FixSelected();

        // Assert
        requested.Should().Equal(fixable);
        vm.Rows.Select(r => r.FixText).Should().Equal("✓", string.Empty);
    }

    [Fact]
    public void Fix_and_fix_all_are_disabled_while_another_book_operation_is_busy_and_enabled_again_once_it_is_not()
    {
        // Arrange
        ValidationResultsViewModel vm = new();
        ValidationResult fixable = new(ValidationSeverity.Warning, "a.xhtml", -1, -1, "a", "Validation_BareBodyText",
            new BareBodyTextFix("a.xhtml"));
        vm.LoadResults(new[] { fixable });
        vm.SetSelection(vm.Rows.ToList());
        vm.HasFixable.Should().BeTrue("a precondition of this test");
        vm.CanFixSelection.Should().BeTrue("a precondition of this test");

        // Act
        vm.IsBookOperationBusy = true;

        // Assert
        vm.HasFixable.Should().BeFalse();
        vm.CanFixSelection.Should().BeFalse();

        // Act
        vm.IsBookOperationBusy = false;

        // Assert
        vm.HasFixable.Should().BeTrue();
        vm.CanFixSelection.Should().BeTrue();
    }

    [Fact]
    public void A_rule_is_named_after_its_message_without_the_details()
    {
        // Act & Assert
        ValidationResultsViewModel.RuleName("Validation_DeadLink").Should().NotContain("{0}").And.Contain("…");

        // Act & Assert
        ValidationResultsViewModel.RuleName("Unknown_Code").Should().Be("Unknown_Code");
    }
}
