using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="DictionaryManagerViewModel"/>: what the window lists, what the Add and Delete commands do,
/// and the fallback of the primary/secondary dictionary when the one the settings name is deleted.
/// </summary>
public sealed class DictionaryManagerViewModelTests
{
    [Fact]
    public void The_built_in_dictionary_is_listed_and_cannot_be_deleted()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();

        // Act
        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings);

        // Assert
        sut.Dictionaries.Should().ContainSingle();
        sut.SelectedDictionary.Should().NotBeNull();
        sut.SelectedDictionary!.IsBuiltIn.Should().BeTrue();
        sut.SelectedDictionary.Works.Should().BeTrue();
        sut.Files.Should().BeEmpty("the built-in dictionary has no files on disk");
        sut.DeleteCommand.CanExecute(null).Should().BeFalse();
        Directory.Exists(sut.DictionariesDirectory).Should().BeTrue("the folder is created when the manager opens");
    }

    [Fact]
    public async Task Adding_a_dictionary_installs_its_files_and_lists_it_with_their_roles()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        using TempDir source = new("dict-manager-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom", "kot");
        File.WriteAllText(source.Combine("hyph_pl_PL.dic"), "ISO8859-2\ndo1m\n");

        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings)
        {
            AddFilesRequested = () => Task.FromResult<IReadOnlyList<string>>(Directory.GetFiles(source.Path)),
        };

        // Act
        await sut.AddCommand.ExecuteAsync(null);

        // Assert
        sut.Changed.Should().BeTrue();
        sut.Dictionaries.Select(d => d.Name).Should().BeEquivalentTo("en_US", "pl_PL");
        sut.SelectedDictionary!.Name.Should().Be("pl_PL");
        sut.SelectedDictionary.Works.Should().BeTrue();
        sut.Files.Select(f => f.FileName).Should().BeEquivalentTo("pl_PL.aff", "pl_PL.dic", "hyph_pl_PL.dic");
        spell.SpellChecker.Dictionaries().Should().Contain("pl_PL");
    }

    [Fact]
    public async Task A_refused_add_reports_the_problem_and_changes_nothing()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        using TempDir source = new("dict-manager-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        string[] affOnly = { source.Combine("pl_PL.aff") };

        string? reported = null;
        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings)
        {
            AddFilesRequested = () => Task.FromResult<IReadOnlyList<string>>(affOnly),
            ProblemReported = problem =>
            {
                reported = problem;
                return Task.CompletedTask;
            },
        };

        // Act
        await sut.AddCommand.ExecuteAsync(null);

        // Assert
        reported.Should().NotBeNull().And.Contain("pl_PL.dic");
        sut.Changed.Should().BeFalse();
        sut.Dictionaries.Should().ContainSingle();
        Directory.GetFiles(sut.DictionariesDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_the_primary_dictionary_falls_back_to_the_built_in_one()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        (SettingsStore settings, SpellChecker spellChecker) = spell;
        DictionaryFixture.Write(spell.HunspellDirectory, "pl_PL", "dom");
        DictionaryFixture.Write(spell.HunspellDirectory, "de_DE", "Haus");
        spellChecker.Reload();
        spellChecker.SetPrimaryDictionary("pl_PL");
        spellChecker.SetSecondaryDictionary("de_DE");

        List<string> confirmed = new();
        DictionaryManagerViewModel sut = new(spellChecker, settings)
        {
            ConfirmDeleteRequested = (_, files) =>
            {
                confirmed.AddRange(files);
                return Task.FromResult(true);
            },
        };
        sut.SelectedDictionary = sut.Dictionaries.Single(d => d.Name == "pl_PL");

        // Act
        await sut.DeleteCommand.ExecuteAsync(null);

        // Assert
        confirmed.Should().BeEquivalentTo("pl_PL.aff", "pl_PL.dic");
        settings.Dictionary.Should().Be("en_US");
        settings.SecondaryDictionary.Should().Be("de_DE", "the secondary dictionary is still installed");
        sut.Dictionaries.Select(d => d.Name).Should().BeEquivalentTo("en_US", "de_DE");
        spellChecker.Dictionaries().Should().NotContain("pl_PL");

        // Act
        sut.SelectedDictionary = sut.Dictionaries.Single(d => d.Name == "de_DE");
        await sut.DeleteCommand.ExecuteAsync(null);

        // Assert
        settings.SecondaryDictionary.Should().BeEmpty("a deleted secondary dictionary leaves none");
        sut.Dictionaries.Should().ContainSingle();
    }

    [Fact]
    public async Task A_delete_that_is_not_confirmed_removes_nothing()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        DictionaryFixture.Write(spell.HunspellDirectory, "pl_PL", "dom");
        spell.SpellChecker.Reload();

        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings)
        {
            ConfirmDeleteRequested = (_, _) => Task.FromResult(false),
        };
        sut.SelectedDictionary = sut.Dictionaries.Single(d => d.Name == "pl_PL");

        // Act
        await sut.DeleteCommand.ExecuteAsync(null);

        // Assert
        sut.Changed.Should().BeFalse();
        sut.Dictionaries.Select(d => d.Name).Should().Contain("pl_PL");
        File.Exists(Path.Combine(sut.DictionariesDirectory, "pl_PL.dic")).Should().BeTrue();
    }

    [Fact]
    public async Task Adding_a_dictionary_that_is_already_installed_asks_before_replacing_it()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        DictionaryFixture.Write(spell.HunspellDirectory, "pl_PL", "dom");
        spell.SpellChecker.Reload();

        using TempDir source = new("dict-manager-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom", "rower");
        bool asked = false;
        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings)
        {
            AddFilesRequested = () => Task.FromResult<IReadOnlyList<string>>(Directory.GetFiles(source.Path)),
            ConfirmReplaceRequested = _ =>
            {
                asked = true;
                return Task.FromResult(false);
            },
        };

        // Act
        await sut.AddCommand.ExecuteAsync(null);

        // Assert
        asked.Should().BeTrue();
        sut.Changed.Should().BeFalse();
        File.ReadAllText(Path.Combine(sut.DictionariesDirectory, "pl_PL.dic")).Should().NotContain("rower");

        // Act
        sut.ConfirmReplaceRequested = _ => Task.FromResult(true);
        await sut.AddCommand.ExecuteAsync(null);

        // Assert
        sut.Changed.Should().BeTrue();
        File.ReadAllText(Path.Combine(sut.DictionariesDirectory, "pl_PL.dic")).Should().Contain("rower");
    }

    /// <summary>
    /// A pair put into the folder by hand that cannot be loaded is listed with the reason, so the user can see it and
    /// delete it — the Delete command works on it like on any other installed dictionary.
    /// </summary>
    [Fact]
    public void A_broken_dictionary_is_listed_with_the_reason_and_can_be_deleted()
    {
        // Arrange
        using SpellCheckSetup spell = SpellCheckTestFactory.New();
        DictionaryFixture.Write(spell.HunspellDirectory, "pl_PL", "dom");
        File.WriteAllText(Path.Combine(spell.HunspellDirectory, "pl_PL.dic"), "not a word count at all\n");

        // Act
        DictionaryManagerViewModel sut = new(spell.SpellChecker, spell.Settings);
        sut.SelectedDictionary = sut.Dictionaries.Single(d => d.Name == "pl_PL");

        // Assert
        sut.SelectedDictionary.Works.Should().BeFalse();
        sut.SelectedDictionary.StateText.Should().Contain("pl_PL.dic");
        sut.DeleteCommand.CanExecute(null).Should().BeTrue();
    }
}
