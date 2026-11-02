using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The "Hyphenation" dialog (<see cref="SoftHyphensViewModel"/>): adding and removing soft hyphens.</summary>
public sealed class SoftHyphensViewModelTests : IDisposable
{
    private const string Shy = SoftHyphenInserter.SoftHyphenEntity;
    private const string Polish = "Text/polish.xhtml";
    private const string NoLanguage = "Text/none.xhtml";

    private readonly TempDir _patterns = new("hyph-app-");
    private readonly List<(string BookPath, int Offset)> _navigated = new();

    public SoftHyphensViewModelTests()
    {
        // English (US) and Polish are installed; German is not.
        File.WriteAllText(_patterns.Combine("hyph_en_US.dic"), TestHyphenation.LiangPatterns);
        File.WriteAllText(_patterns.Combine("hyph_pl_PL.dic"), TestHyphenation.BeforeEveryCPatterns);
    }

    public void Dispose() => _patterns.Dispose();

    private static string Page(string body, string lang = "") =>
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"" + (lang.Length > 0 ? $" lang=\"{lang}\"" : string.Empty)
        + "><head><title>T</title></head><body>" + body + "</body></html>";

    // A Polish page with English (en-GB, only en-US installed) and German (none installed) in it, and a page with no
    // language at all (the book states none either).
    private static SoftHyphenSurvey Book() => SoftHyphenSurvey.Of(
        [
            (Polish, Page(
                $"<p>cierpliwości niezależność podzie{Shy}lone</p><p lang=\"en-GB\">hyphenation</p>"
                + $"<p lang=\"de\">Unabhängigkeit</p><p><img alt=\"a{Shy}lt\" src=\"x.png\"/></p>",
                "pl")),
            (NoLanguage, Page("<p>zzzzzzzzzz</p>")),
        ],
        string.Empty);

    // The plans applied, and the survey each answers (null = it did not run).
    private readonly List<object> _applied = new();
    private SoftHyphenSurvey? _afterApply;

    private SoftHyphensViewModel New(SoftHyphensMode mode, SoftHyphenSurvey? survey = null) =>
        new(survey ?? Book(), () => new HyphenationPatternFiles(_patterns.Path), (path, offset) => _navigated.Add((path, offset)),
            plan =>
            {
                _applied.Add(plan);
                return Task.FromResult(_afterApply);
            },
            plan =>
            {
                _applied.Add(plan);
                return Task.FromResult(_afterApply);
            })
        {
            Mode = mode,
        };

    private static SoftHyphensLanguageNode Language(SoftHyphensViewModel sut, string key) =>
        sut.Roots.OfType<SoftHyphensLanguageNode>().Single(l => l.Language.Key == key);

    [Fact]
    public void Adding_lists_every_language_and_checks_those_with_patterns()
    {
        // Act
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);

        // Assert
        sut.Roots.OfType<SoftHyphensLanguageNode>().Select(l => l.Language.Key).Should().Equal("pl", "en-gb", "de", string.Empty);
        Language(sut, "pl").Check.Should().BeTrue();
        Language(sut, "pl").Patterns.Should().Be("hyph_pl_PL.dic");
        Language(sut, "pl").PatternsWarn.Should().BeFalse();
        Language(sut, "en-gb").Check.Should().BeTrue("patterns of another variant are used");
        Language(sut, "en-gb").PatternsWarn.Should().BeTrue();
        Language(sut, "en-gb").Patterns.Should().Be(Strings.Format("SoftHyphens_PatternsFallback", "hyph_en_GB.dic", "hyph_en_US.dic"));
        Language(sut, "de").HasCheckBox.Should().BeFalse();
        Language(sut, "de").Patterns.Should().Be(Strings.Format("SoftHyphens_PatternsMissing", "hyph_de_*.dic"));
        Language(sut, "de").Children.Should().OnlyContain(f => !f.HasCheckBox, "the files of a language without patterns cannot be checked");
        Language(sut, string.Empty).HasCheckBox.Should().BeFalse("the unknown language has no patterns");
        Language(sut, string.Empty).Title.Should().Be(Strings.Get("SoftHyphens_UnknownLanguage"));
        sut.HasLanguagesWithoutPatterns.Should().BeTrue();
        sut.CanApply.Should().BeTrue();
    }

    [Fact]
    public void Each_language_says_where_the_book_states_it()
    {
        // Act
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);

        // Assert
        Language(sut, "pl").Source.Should().Be(Strings.Format("SoftHyphens_SourceFiles", Strings.Format("SoftHyphens_SourceElement", "html"), 1));
        Language(sut, "de").Source.Should().Be(Strings.Format("SoftHyphens_SourceFiles", Strings.Format("SoftHyphens_SourceElement", "p"), 1));
        Language(sut, string.Empty).Source.Should().Be(Strings.Format("SoftHyphens_SourceFiles", Strings.Get("SoftHyphens_SourceNone"), 1));
    }

    [Fact]
    public void The_plan_of_adding_is_the_checked_files_with_the_patterns_of_their_language()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);

        // Act
        SoftHyphenAddPlan plan = sut.AddPlan();

        // Assert
        plan.Chosen.Should().BeEquivalentTo(new[] { (Polish, "pl"), (Polish, "en-gb") });
        plan.Patterns.Keys.Should().BeEquivalentTo("pl", "en-gb");
        plan.MinWordLength.Should().Be(SoftHyphenInserter.DefaultMinWordLength);
    }

    [Fact]
    public void A_language_with_some_of_its_files_unchecked_is_neither_checked_nor_unchecked()
    {
        // Arrange
        SoftHyphenSurvey survey = SoftHyphenSurvey.Of(
            [("Text/a.xhtml", Page("<p>cierpliwości</p>", "pl")), ("Text/b.xhtml", Page("<p>cierpliwości</p>", "pl"))],
            string.Empty);
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add, survey);
        SoftHyphensLanguageNode polish = Language(sut, "pl");

        // Act
        polish.Children[0].Check = false;

        // Assert
        polish.Check.Should().BeNull();
        sut.AddPlan().Chosen.Should().Equal(("Text/b.xhtml", "pl"));

        // Act
        polish.Check = true;

        // Assert
        polish.Children.Should().OnlyContain(f => f.Check == true);
    }

    [Fact]
    public void Select_all_and_deselect_all_switch_every_row_that_can_be_checked()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);

        // Act
        sut.DeselectAllCommand.Execute(null);

        // Assert
        sut.CanApply.Should().BeFalse();
        sut.ApplyCommand.CanExecute(null).Should().BeFalse();
        sut.AddPlan().Chosen.Should().BeEmpty();

        // Act
        sut.SelectAllCommand.Execute(null);

        // Assert
        sut.CanApply.Should().BeTrue();
        Language(sut, "de").Check.Should().BeFalse("it still cannot be hyphenated");
        sut.AddPlan().Chosen.Should().HaveCount(2);
    }

    [Fact]
    public void The_shortest_hyphenated_word_changes_the_counts_and_the_plan()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);
        SoftHyphensLanguageNode polish = Language(sut, "pl");
        List<string?> changed = new();
        ((INotifyPropertyChanged)polish).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        sut.MinWordLength = 12;

        // Assert
        changed.Should().Contain(nameof(SoftHyphensNode.Count));
        polish.Count.Should().Be(2.ToString(CultureInfo.CurrentCulture), "\"cierpliwości\" and \"niezależność\" have 12 letters");
        sut.AddPlan().MinWordLength.Should().Be(12);

        // Act
        sut.MinWordLength = 1000;

        // Assert
        sut.MinWordLength.Should().Be((int)SoftHyphensViewModel.MinWordLengthMax);
    }

    [Fact]
    public void Removing_lists_the_languages_with_soft_hyphens_and_the_places_outside_the_text()
    {
        // Act
        SoftHyphensViewModel sut = New(SoftHyphensMode.Remove);

        // Assert
        sut.Roots.OfType<SoftHyphensLanguageNode>().Select(l => l.Language.Key).Should().Equal("pl");
        SoftHyphensGroupNode outside = sut.Roots.OfType<SoftHyphensGroupNode>().Single();
        outside.Title.Should().Be(Strings.Get("SoftHyphens_OutsideText"));
        SoftHyphensNode alt = outside.Children.Single();
        alt.Title.Should().Be(Strings.Format("SoftHyphens_KindCount", Strings.Format("SoftHyphens_PlaceAttribute", "alt"), 1));
        alt.Children.Single().Title.Should().Be(Strings.Format("SoftHyphens_PlaceTitle", Polish, 1));
        alt.Children.Single().Context.Should().Contain("alt=");
        sut.Roots.Should().OnlyContain(r => r.Check == true, "everything is checked to start with");
    }

    [Fact]
    public void The_plan_of_removing_is_the_checked_files_and_places()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Remove);
        SoftHyphensPlaceNode place = sut.Roots.SelectMany(r => r.Children).SelectMany(c => c.Children).OfType<SoftHyphensPlaceNode>().Single();

        // Act
        SoftHyphenRemovePlan all = sut.RemovePlan();
        place.Check = false;
        SoftHyphenRemovePlan withoutPlace = sut.RemovePlan();

        // Assert
        all.Chosen.Should().Equal((Polish, "pl"));
        all.OutsideText.Should().ContainSingle().Which.Name.Should().Be("alt");
        withoutPlace.OutsideText.Should().BeEmpty();
        withoutPlace.Chosen.Should().Equal((Polish, "pl"));
    }

    [Fact]
    public void A_file_or_a_place_opens_where_it_is()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Remove);
        SoftHyphensPlaceNode place = sut.Roots.SelectMany(r => r.Children).SelectMany(c => c.Children).OfType<SoftHyphensPlaceNode>().Single();
        SoftHyphensNode file = Language(sut, "pl").Children.Single();

        // Act
        bool placeOpened = place.Navigate();
        bool fileOpened = file.Navigate();

        // Assert
        placeOpened.Should().BeTrue();
        fileOpened.Should().BeTrue();
        _navigated.Should().Equal((Polish, place.Place.Offset), (Polish, 0));
        Language(sut, "pl").Navigate().Should().BeFalse("a language is no place");
    }

    [Fact]
    public async Task Patterns_installed_in_the_dictionary_manager_make_a_language_checkable()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);
        sut.DictionaryManager = () =>
        {
            File.WriteAllText(_patterns.Combine("hyph_de_DE.dic"), TestHyphenation.BeforeEveryCPatterns);
            return Task.FromResult(true);
        };

        // Act
        await sut.OpenDictionaryManagerCommand.ExecuteAsync(null);

        // Assert
        Language(sut, "de").HasCheckBox.Should().BeTrue();
        Language(sut, "de").Patterns.Should().Be("hyph_de_DE.dic");
    }

    [Fact]
    public void The_switch_shows_the_rows_of_its_side_each_with_its_own_checks()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);
        Language(sut, "pl").Check = false;

        // Act
        sut.IsRemoveMode = true;

        // Assert
        sut.Mode.Should().Be(SoftHyphensMode.Remove);
        sut.Roots.Should().BeSameAs(sut.RemoveRoots);
        sut.ApplyText.Should().Be(Strings.Get("SoftHyphens_Remove"));
        Language(sut, "pl").Check.Should().BeTrue("removing has its own checks");

        // Act
        sut.IsAddMode = true;

        // Assert
        sut.Roots.Should().BeSameAs(sut.AddRoots);
        Language(sut, "pl").Check.Should().BeFalse("adding kept its checks");
    }

    [Fact]
    public async Task Applying_leaves_the_dialog_open_on_the_book_surveyed_anew()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Remove);
        bool closed = false;
        sut.CloseRequested += (_, _) => closed = true;
        _afterApply = SoftHyphenSurvey.Of([(Polish, Page("<p>cierpliwości</p>", "pl"))], string.Empty);

        // Act
        await sut.ApplyCommand.ExecuteAsync(null);

        // Assert
        _applied.Should().ContainSingle().Which.Should().BeOfType<SoftHyphenRemovePlan>();
        closed.Should().BeFalse();
        sut.RemoveRoots.Should().BeEmpty("the soft hyphens are gone");
        sut.IsEmpty.Should().BeTrue();
        sut.EmptyText.Should().Be(Strings.Get("SoftHyphens_NothingToRemove"));
        sut.AddRoots.OfType<SoftHyphensLanguageNode>().Select(l => l.Language.Key).Should().Equal("pl");
    }

    [Fact]
    public async Task A_plan_that_did_not_run_leaves_the_rows_as_they_were()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);
        SoftHyphensNode before = sut.AddRoots[0];

        // Act
        await sut.ApplyCommand.ExecuteAsync(null);

        // Assert
        _applied.Should().ContainSingle().Which.Should().BeOfType<SoftHyphenAddPlan>();
        sut.AddRoots[0].Should().BeSameAs(before);
    }

    [Fact]
    public void Nothing_is_applied_while_another_operation_on_the_book_runs()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);

        // Act
        sut.IsBookOperationBusy = true;

        // Assert
        sut.CanApply.Should().BeFalse();
        sut.ApplyCommand.CanExecute(null).Should().BeFalse();

        // Act
        sut.IsBookOperationBusy = false;

        // Assert
        sut.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Close_closes_the_dialog()
    {
        // Arrange
        SoftHyphensViewModel sut = New(SoftHyphensMode.Add);
        int closed = 0;
        sut.CloseRequested += (_, _) => closed++;

        // Act
        sut.CloseCommand.Execute(null);

        // Assert
        closed.Should().Be(1);
    }
}
