using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the "Spellcheck" page in <see cref="PreferencesViewModel"/>: managing user
/// dictionaries and their word lists.
/// </summary>
public sealed class PreferencesSpellcheckTests : IDisposable
{
    private static readonly string[] Mimsy = { "mimsy" };
    private static readonly string[] ZzyzxAndBrillig = { "zzyzx", "brillig" };

    private readonly TestHost _host = new();
    private readonly SpellChecker _spellChecker;
    private readonly PreferencesViewModel _vm;
    private readonly Queue<string?> _answers = new();
    private readonly List<string> _errors = new();

    public PreferencesSpellcheckTests()
    {
        ThemeManager theme = new(_host.Settings, NullLogger<ThemeManager>.Instance);
        LocalizationManager localization = new(_host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(_host.Settings, NullLogger<IconThemeManager>.Instance);
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-spell-" + Guid.NewGuid().ToString("N"));
        _spellChecker = new SpellChecker(_host.Settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        UiDensityManager uiDensity = new(
            _host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        _vm = new PreferencesViewModel(
            _host.Settings, _spellChecker, theme, localization, iconTheme, _host.Shortcuts, _host.Registry, uiDensity)
        {
            TextPrompt = (_, _, _) => Task.FromResult(_answers.Dequeue()),
            ErrorMessage = (_, message) =>
            {
                _errors.Add(message);
                return Task.CompletedTask;
            },
        };
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Default_user_dictionary_exists_and_is_selected_on_first_open()
    {
        _vm.UserDictionaries.Select(d => d.Name).Should().Equal(SpellChecker.DefaultUserDictionaryName);
        _vm.SelectedUserDictionary!.Name.Should().Be("default");
        _vm.DefaultUserDictionaryText.Should().Be(Strings.Format("PreferencesWindow_DefaultUserDictionary", "default"));
    }

    [Fact]
    public void Dictionaries_are_shown_by_language_name_and_secondary_can_be_cleared()
    {
        _vm.AvailableDictionaries.Should().Contain(new DictionaryOption("en_US", SpellChecker.DisplayName("en_US")));
        _vm.SelectedPrimaryDictionary!.Name.Should().Be(_host.Settings.Dictionary);
        _vm.AvailableSecondaryDictionaries[^1].Name.Should().BeEmpty();

        _vm.SelectedSecondaryDictionary = _vm.AvailableSecondaryDictionaries.Single(d => d.Name == "en_US");
        _vm.ApplyCommand.Execute(null);
        _host.Settings.SecondaryDictionary.Should().Be("en_US");

        _vm.SelectedSecondaryDictionary = _vm.AvailableSecondaryDictionaries[^1];
        _vm.ApplyCommand.Execute(null);
        _host.Settings.SecondaryDictionary.Should().BeEmpty();
    }

    [Theory]
    [AutoData]
    public async Task Add_creates_an_enabled_dictionary_and_makes_it_the_default(string name)
    {
        _answers.Enqueue(name);

        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);

        _vm.UserDictionaries.Should().Contain(d => d.Name == name && d.IsEnabled);
        _vm.SelectedUserDictionary!.Name.Should().Be(name);
        _vm.ApplyCommand.Execute(null);
        _host.Settings.DefaultUserDictionary.Should().Be(name);
        _host.Settings.EnabledUserDictionaries.Should().Contain(name);
    }

    [Fact]
    public async Task Dictionary_operations_happen_at_once_and_applying_later_keeps_them()
    {
        _answers.Enqueue("alice");
        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);
        _vm.UserDictionaries.Single(d => d.Name == "alice").IsEnabled = false;
        _host.Settings.EnabledUserDictionaries.Should().Contain("alice", "disabling waits for Save / Apply");

        _answers.Enqueue("bob");
        await _vm.RenameUserDictionaryCommand.ExecuteAsync(null);

        _spellChecker.UserDictionaries().Should().Contain("bob").And.NotContain("alice");
        _vm.UserDictionaries.Single(d => d.Name == "bob").IsEnabled.Should().BeFalse();

        _vm.ApplyCommand.Execute(null);

        _host.Settings.EnabledUserDictionaries.Should().NotContain("alice").And.NotContain("bob");
        _host.Settings.DefaultUserDictionary.Should().Be("bob");
    }

    [Fact]
    public async Task Add_with_an_existing_name_shows_an_error_and_changes_nothing()
    {
        _answers.Enqueue("DEFAULT");

        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);

        _errors.Should().Equal(Strings.Get("PreferencesWindow_DictionaryExists"));
        _vm.UserDictionaries.Should().ContainSingle();
    }

    [Fact]
    public async Task Add_with_an_invalid_name_shows_an_error()
    {
        _answers.Enqueue("a/b");

        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);

        _errors.Should().Equal(Strings.Get("PreferencesWindow_DictionaryNameInvalid"));
    }

    [Theory]
    [AutoData]
    public async Task Rename_renames_the_selected_dictionary_and_keeps_it_default(string name)
    {
        _spellChecker.AddToUserDictionary("Wonderland");
        _answers.Enqueue(name);

        await _vm.RenameUserDictionaryCommand.ExecuteAsync(null);

        _vm.UserDictionaries.Select(d => d.Name).Should().Equal(name);
        _host.Settings.DefaultUserDictionary.Should().Be(name);
        _vm.UserWords.Should().Equal("Wonderland");
    }

    [Fact]
    public async Task Copy_duplicates_the_selected_dictionary_with_its_words()
    {
        _spellChecker.AddToUserDictionary("Wonderland");

        await _vm.CopyUserDictionaryCommand.ExecuteAsync(null);

        _vm.UserDictionaries.Select(d => d.Name).Should().Equal("default", "default_copy");
        _vm.SelectedUserDictionary!.Name.Should().Be("default_copy");
        _vm.UserWords.Should().Equal("Wonderland");
    }

    [Fact]
    public async Task Remove_refuses_the_last_dictionary()
    {
        await _vm.RemoveUserDictionaryCommand.ExecuteAsync(null);

        _errors.Should().Equal(Strings.Get("PreferencesWindow_CannotRemoveLastDictionary"));
        _vm.UserDictionaries.Should().ContainSingle();
    }

    [Theory]
    [AutoData]
    public async Task Remove_deletes_the_selected_dictionary_and_selects_the_new_default(string name)
    {
        _answers.Enqueue(name);
        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);

        await _vm.RemoveUserDictionaryCommand.ExecuteAsync(null);

        _vm.UserDictionaries.Select(d => d.Name).Should().Equal("default");
        _vm.SelectedUserDictionary!.Name.Should().Be("default");
        _host.Settings.DefaultUserDictionary.Should().Be("default");
    }

    [Fact]
    public async Task Selecting_a_dictionary_makes_it_default_and_shows_its_words()
    {
        _spellChecker.CreateUserDictionary("alice");
        _spellChecker.AddToUserDictionary("Cheshire", "alice");
        _answers.Enqueue("people");
        await _vm.AddUserDictionaryCommand.ExecuteAsync(null);

        _vm.SelectedUserDictionary = _vm.UserDictionaries.Single(d => d.Name == "alice");
        _vm.ApplyCommand.Execute(null);

        _host.Settings.DefaultUserDictionary.Should().Be("alice");
        _vm.UserWords.Should().Equal("Cheshire");
    }

    [Fact]
    public async Task Word_list_supports_add_edit_remove_and_remove_all()
    {
        _answers.Enqueue("zzyzx, brillig  mimsy");
        await _vm.AddUserWordsCommand.ExecuteAsync(null);
        _vm.UserWords.Should().Equal("brillig", "mimsy", "zzyzx");
        _spellChecker.Check("zzyzx").Should().BeTrue();

        _vm.SetSelectedUserWords(Mimsy);
        _answers.Enqueue("borogove");
        await _vm.EditUserWordCommand.ExecuteAsync(null);
        _vm.UserWords.Should().Equal("borogove", "brillig", "zzyzx");

        _vm.SetSelectedUserWords(ZzyzxAndBrillig);
        await _vm.RemoveUserWordsCommand.ExecuteAsync(null);
        _vm.UserWords.Should().Equal("borogove");
        _spellChecker.Check("zzyzx").Should().BeFalse();

        await _vm.RemoveAllUserWordsCommand.ExecuteAsync(null);
        _vm.UserWords.Should().BeEmpty();
        _spellChecker.UserDictionaryWords("default").Should().BeEmpty();
    }

    [Fact]
    public void Word_commands_need_a_selection()
    {
        _vm.EditUserWordCommand.CanExecute(null).Should().BeFalse();
        _vm.RemoveUserWordsCommand.CanExecute(null).Should().BeFalse();
        _vm.RemoveAllUserWordsCommand.CanExecute(null).Should().BeFalse();
    }
}
