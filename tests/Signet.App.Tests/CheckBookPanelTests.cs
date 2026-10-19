using System;
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
    public void Fix_all_applies_the_fixes_after_a_checkpoint_and_checks_the_book_again()
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

            // Assert
            vm.ValidationResults.Rows.Should().Contain(r => r.Result.Code == "Validation_BareBodyText" && r.CanFix);
            vm.ValidationResults.HasFixable.Should().BeTrue();

            // Act
            vm.ValidationResults.FixAll();

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
    }

    [Fact]
    public void A_skipped_type_of_problem_is_left_out_until_it_is_restored()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, _) = Load(temp);
        try
        {
            vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            ValidationResultRow font = vm.ValidationResults.Rows.Single(r => r.Result.Code == DummyFontCode);

            // Act
            vm.ValidationResults.SkipRule(font);

            // Assert
            vm.ValidationResults.Rows.Should().NotContain(r => r.Result.Code == DummyFontCode);
            vm.ValidationResults.SkippedRules.Select(r => r.Code).Should().Equal(DummyFontCode);
            vm.ValidationResults.HasSkippedRules.Should().BeTrue();

            // Act
            vm.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);

            // Assert
            vm.ValidationResults.Rows.Should().NotContain(r => r.Result.Code == DummyFontCode, "the skip is remembered");

            // Act
            vm.ValidationResults.RestoreRules(new[] { DummyFontCode });

            // Assert
            vm.ValidationResults.Rows.Should().Contain(r => r.Result.Code == DummyFontCode);
            vm.ValidationResults.HasSkippedRules.Should().BeFalse();
        }
        finally
        {
            vm.CheckpointHistory.Dispose();
        }
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
    public void A_rule_is_named_after_its_message_without_the_details()
    {
        // Act & Assert
        ValidationResultsViewModel.RuleName("Validation_DeadLink").Should().NotContain("{0}").And.Contain("…");

        // Act & Assert
        ValidationResultsViewModel.RuleName("Unknown_Code").Should().Be("Unknown_Code");
    }
}
