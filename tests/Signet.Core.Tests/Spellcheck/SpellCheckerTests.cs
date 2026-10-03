using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Spellcheck;

/// <summary>
/// Tests for <see cref="SpellChecker"/> — check/suggest (primary + secondary dictionary, by language
/// code), detecting installed dictionaries (and their precedence over the embedded en_US),
/// user dictionaries, ignored words.
/// </summary>
public sealed class SpellCheckerTests : EnglishUiCultureTest
{
    private static readonly string[] GermanLangs = { "de" };
    private static readonly string[] FrenchLangs = { "fr" };
    private static readonly string[] UnknownLangs = { "xx" };
    private static readonly string[] MessyWords = { "zzyzx", " Brillig ", "", "zzyzx", "ta’wrong" };
    private static readonly string[] NewWordOnly = { "zzyzx" };

    [Fact]
    public void Check_and_suggest_use_the_configured_primary_dictionary()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "test_EN", "hello", "world");
        SettingsStore settings = NewSettings(dicts, "test_EN");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("hello").Should().BeTrue();
        sut.Check("helllo").Should().BeFalse();
        sut.Suggest("helllo").Should().Contain("hello");
    }

    [Fact]
    public void Embedded_en_US_dictionary_detects_errors_and_suggests()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("hello").Should().BeTrue();
        sut.Check("wrlod").Should().BeFalse();
        sut.Suggest("wrlod").Should().Contain("world");
    }

    [Fact]
    public void Installed_dictionary_overrides_embedded_one_with_the_same_name()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "en_US", "onlyword");
        SettingsStore settings = NewSettings(dicts, "en_US");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("onlyword").Should().BeTrue();
        // "hello" exists in the real embedded en_US, but not in the installed version, which takes
        // precedence — proof that the file from the directory was used, not the embedded resource.
        sut.Check("hello").Should().BeFalse();
    }

    [Fact]
    public void Check_falls_back_to_secondary_dictionary()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "primary_X", "apple");
        WriteDictionary(dicts.Path, "secondary_X", "banana");
        SettingsStore settings = NewSettings(dicts, "primary_X");
        settings.SecondaryDictionary = "secondary_X";

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("apple").Should().BeTrue();
        sut.Check("banana").Should().BeTrue();
        sut.Check("cherry").Should().BeFalse();
    }

    [Fact]
    public void Check_with_langs_resolves_dictionary_by_two_letter_language_code()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "de_DE", "hund", "katze");
        WriteDictionary(dicts.Path, "fr_FR", "chat", "chien");
        SettingsStore settings = NewSettings(dicts, "de_DE");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("hund", GermanLangs).Should().BeTrue();
        sut.Check("chat", FrenchLangs).Should().BeTrue();
        // "chat" does not exist in the German dictionary, and since it is checked against it (code "de"
        // resolved to the first matching dictionary), it is considered misspelled.
        sut.Check("chat", GermanLangs).Should().BeFalse();
    }

    [Fact]
    public void Check_with_unresolvable_lang_is_treated_as_correct()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("totalnonsenseword", UnknownLangs).Should().BeTrue();
    }

    [Fact]
    public void Ignored_words_are_treated_as_correct_until_cleared()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "test_EN", "hello", "world");
        SettingsStore settings = NewSettings(dicts, "test_EN");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("helllo").Should().BeFalse();

        sut.IgnoreWord("helllo");
        sut.IsIgnored("helllo").Should().BeTrue();
        sut.Check("helllo").Should().BeTrue();

        sut.ClearIgnoredWords();
        sut.IsIgnored("helllo").Should().BeFalse();
        sut.Check("helllo").Should().BeFalse();
    }

    [Fact]
    public void AddToUserDictionary_marks_word_correct_immediately_and_persists_it()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "test_EN", "hello");
        SettingsStore settings = NewSettings(dicts, "test_EN");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Check("newword").Should().BeFalse();
        sut.AddToUserDictionary("newword");
        sut.Check("newword").Should().BeTrue();
        sut.UserDictionaryWords(settings.DefaultUserDictionary).Should().Contain("newword");

        // A new instance (as after a restart) — the word is loaded from the user dictionary at startup.
        SpellChecker reopened = new(settings, dicts.Path, userDicts.Path);
        reopened.Check("newword").Should().BeTrue();
    }

    [Fact]
    public void AddToUserDictionary_does_not_duplicate_an_existing_word()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "test_EN", "hello");
        SettingsStore settings = NewSettings(dicts, "test_EN");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.AddToUserDictionary("newword");
        sut.AddToUserDictionary("newword");

        sut.UserDictionaryWords(settings.DefaultUserDictionary)
            .Count(w => w == "newword").Should().Be(1);
    }

    [Fact]
    public void Dictionaries_lists_embedded_and_installed_names_sorted()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "zz_ZZ", "word");
        WriteDictionary(dicts.Path, "aa_AA", "word");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.Dictionaries().Should().Equal("aa_AA", "en_US", "zz_ZZ");
    }

    [Fact]
    public void UserDictionaries_lists_files_in_the_user_dictionary_directory()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.AddToUserDictionary("word", "custom");
        sut.AddToUserDictionary("other", "default");

        sut.UserDictionaries().Should().Equal("custom", "default");
    }

    [Fact]
    public void SetPrimaryDictionary_switches_the_active_word_list_and_persists_the_setting()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        WriteDictionary(dicts.Path, "dictA", "apple");
        WriteDictionary(dicts.Path, "dictB", "banana");
        SettingsStore settings = NewSettings(dicts, "dictA");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);
        sut.Check("apple").Should().BeTrue();

        sut.SetPrimaryDictionary("dictB");

        sut.PrimaryDictionaryName.Should().Be("dictB");
        settings.Dictionary.Should().Be("dictB");
        sut.Check("banana").Should().BeTrue();
        sut.Check("apple").Should().BeFalse();
    }

    [Fact]
    public void Constructor_creates_the_default_user_dictionary_file_when_missing()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");

        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.UserDictionaries().Should().Equal(SpellChecker.DefaultUserDictionaryName);
        File.ReadAllText(userDicts.Combine("default")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en_GB", "English - Great Britain")]
    [InlineData("en", "English")]
    [InlineData("de_DE_frami", "German - Germany - frami")]
    [InlineData("fr_ZZ", "French - ZZ")]
    [InlineData("xx_YY", "xx_YY")]
    public void DisplayName_builds_the_language_name(string dictionary, string expected)
    {
        SpellChecker.DisplayName(dictionary).Should().Be(expected);
    }

    [Theory]
    [InlineData("", UserDictionaryNameProblem.Empty)]
    [InlineData("   ", UserDictionaryNameProblem.Empty)]
    [InlineData("a/b", UserDictionaryNameProblem.InvalidCharacters)]
    [InlineData("a:b", UserDictionaryNameProblem.InvalidCharacters)]
    [InlineData(" names", UserDictionaryNameProblem.InvalidCharacters)]
    [InlineData("names.", UserDictionaryNameProblem.InvalidCharacters)]
    [InlineData("DEFAULT", UserDictionaryNameProblem.AlreadyExists)]
    [InlineData("people names", UserDictionaryNameProblem.None)]
    public void ValidateUserDictionaryName_reports_the_problem(string name, UserDictionaryNameProblem expected)
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SpellChecker sut = new(NewSettings(dicts, "en_US"), dicts.Path, userDicts.Path);

        sut.ValidateUserDictionaryName(name).Should().Be(expected);
    }

    [Fact]
    public void ValidateUserDictionaryName_allows_the_current_name_when_renaming()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SpellChecker sut = new(NewSettings(dicts, "en_US"), dicts.Path, userDicts.Path);

        sut.ValidateUserDictionaryName("Default", currentName: "default").Should().Be(UserDictionaryNameProblem.None);
    }

    [Fact]
    public void CreateUserDictionary_creates_an_empty_enabled_dictionary()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);

        sut.CreateUserDictionary("slang");

        sut.UserDictionaries().Should().Equal("default", "slang");
        settings.EnabledUserDictionaries.Should().Contain("slang");
        sut.UserDictionaryWords("slang").Should().BeEmpty();
        sut.Invoking(s => s.CreateUserDictionary("Slang")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RenameUserDictionary_moves_the_file_the_enabled_flag_and_the_default_role()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);
        sut.AddToUserDictionary("Wonderland");

        sut.RenameUserDictionary("default", "alice");

        sut.UserDictionaries().Should().Equal("alice");
        sut.UserDictionaryWords("alice").Should().Equal("Wonderland");
        settings.EnabledUserDictionaries.Should().Equal("alice");
        sut.DefaultUserDictionary.Should().Be("alice");
    }

    [Fact]
    public void CopyUserDictionary_copies_words_under_the_first_free_copy_name()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);
        sut.AddToUserDictionary("Wonderland");
        sut.CreateUserDictionary("default_copy");

        string copy = sut.CopyUserDictionary("default");

        copy.Should().Be("default_copy_copy");
        sut.UserDictionaryWords(copy).Should().Equal("Wonderland");
        settings.EnabledUserDictionaries.Should().Contain(copy);
    }

    [Fact]
    public void RemoveUserDictionary_deletes_it_moves_the_default_role_and_forgets_its_words()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker sut = new(settings, dicts.Path, userDicts.Path);
        sut.CreateUserDictionary("alice");
        sut.AddToUserDictionary("Wonderlandish");
        sut.Check("Wonderlandish").Should().BeTrue();

        sut.RemoveUserDictionary("default");

        sut.UserDictionaries().Should().Equal("alice");
        settings.EnabledUserDictionaries.Should().Equal("alice");
        sut.DefaultUserDictionary.Should().Be("alice");
        sut.Check("Wonderlandish").Should().BeFalse();
    }

    [Fact]
    public void RemoveUserDictionary_refuses_to_remove_the_last_dictionary()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SpellChecker sut = new(NewSettings(dicts, "en_US"), dicts.Path, userDicts.Path);

        sut.Invoking(s => s.RemoveUserDictionary("default")).Should().Throw<InvalidOperationException>();
        sut.UserDictionaries().Should().Equal("default");
    }

    [Fact]
    public void SetUserDictionaryWords_cleans_the_list_and_updates_spelling()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SpellChecker sut = new(NewSettings(dicts, "en_US"), dicts.Path, userDicts.Path);
        sut.AddToUserDictionary("Wonderlandish");

        sut.SetUserDictionaryWords("default", MessyWords);

        sut.UserDictionaryWords("default").Should().Equal("Brillig", "ta'wrong", "zzyzx");
        sut.Check("Wonderlandish").Should().BeFalse();
        sut.Check("zzyzx").Should().BeTrue();
    }

    [Fact]
    public void SetUserDictionaryWords_adding_only_makes_new_words_correct()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        SpellChecker sut = new(NewSettings(dicts, "en_US"), dicts.Path, userDicts.Path);
        sut.Check("zzyzx").Should().BeFalse();

        sut.SetUserDictionaryWords("default", NewWordOnly);

        sut.Check("zzyzx").Should().BeTrue();
    }

    private static SettingsStore NewSettings(TempDir dir, string primaryDictionary)
    {
        SettingsStore settings = new(dir.Combine("settings.json"));
        settings.Dictionary = primaryDictionary;
        return settings;
    }

    private static void WriteDictionary(string directory, string name, params string[] words) =>
        DictionaryFixture.Write(directory, name, words);
}
