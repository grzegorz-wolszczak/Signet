using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Spellcheck;

/// <summary>
/// Tests for <see cref="HunspellDictionaryInstaller"/> — the engine of the dictionary manager: listing what is
/// installed (with the built-in <c>en_US</c> and what overrides it), the three validation steps of a candidate (by
/// name, by shape, by content), installing with and without replacing, and deleting a dictionary with its companion
/// files.
/// </summary>
public sealed class HunspellDictionaryInstallerTests : EnglishUiCultureTest
{
    [Fact]
    public void A_complete_pair_is_installed_and_listed_as_working()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom", "kot");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(PairIn(source, "pl_PL"), target.Path, replace: false);

        // Assert
        result.Installed.Should().BeTrue();
        result.Name.Should().Be("pl_PL");
        result.Problem.Should().BeNull();
        result.RefusedFiles.Should().BeEmpty();

        // Act
        IReadOnlyList<InstalledDictionary> installed = HunspellDictionaryInstaller.List(target.Path);

        // Assert
        InstalledDictionary polish = installed.Single(d => d.Name == "pl_PL");
        polish.Problem.Should().BeNull();
        polish.IsBuiltIn.Should().BeFalse();
        polish.OverridesBuiltIn.Should().BeFalse();
        polish.Files.Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo("pl_PL.aff", "pl_PL.dic");
        polish.Files.Should().OnlyContain(f => f.Role == DictionaryFileRole.Required);
    }

    [Fact]
    public void An_aff_without_its_dic_is_refused_as_incomplete_and_nothing_is_copied()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        string[] affOnly = { source.Combine("pl_PL.aff") };

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(affOnly, target.Path, replace: false);

        // Assert
        result.Installed.Should().BeFalse();
        result.Name.Should().Be("pl_PL");
        result.Problem.Should().Contain("pl_PL.dic");
        Directory.GetFileSystemEntries(target.Path).Should().BeEmpty("a failed add leaves the folder exactly as it was");
    }

    [Fact]
    public void A_dic_without_its_aff_is_refused_as_incomplete_and_nothing_is_copied()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        string[] dicOnly = { source.Combine("pl_PL.dic") };

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(dicOnly, target.Path, replace: false);

        // Assert
        result.Installed.Should().BeFalse();
        result.Name.Should().BeNull();
        result.Problem.Should().Contain(".aff");
        Directory.GetFileSystemEntries(target.Path).Should().BeEmpty();
    }

    [Fact]
    public void A_dic_whose_first_line_is_not_a_word_count_is_refused()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        File.WriteAllText(source.Combine("pl_PL.dic"), "Lorem ipsum dolor sit amet\nand more prose\n");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(PairIn(source, "pl_PL"), target.Path, replace: false);

        // Assert
        result.Installed.Should().BeFalse();
        result.Problem.Should().Contain("pl_PL.dic");
        Directory.GetFileSystemEntries(target.Path).Should().BeEmpty();
    }

    [Fact]
    public void An_aff_with_no_hunspell_directive_is_refused()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        File.WriteAllText(source.Combine("pl_PL.aff"), "This file is only a note about the dictionary.\n");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Validate(PairIn(source, "pl_PL"));

        // Assert
        result.Problem.Should().Contain("pl_PL.aff");
        result.Installed.Should().BeFalse();
    }

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("pl_PL.dic_delta")]
    public void A_file_with_an_unsupported_extension_is_refused_by_name(string fileName)
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        File.WriteAllText(source.Combine(fileName), "x");
        List<string> files = PairIn(source, "pl_PL").Append(source.Combine(fileName)).ToList();

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(files, target.Path, replace: false);

        // Assert
        result.Installed.Should().BeFalse();
        result.RefusedFiles.Should().BeEquivalentTo(fileName);
        result.Problem.Should().Contain(fileName);
        Directory.GetFileSystemEntries(target.Path).Should().BeEmpty();
    }

    [Fact]
    public void The_companion_files_of_a_libre_office_package_are_installed_and_listed_with_their_roles()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom", "kot");
        WritePackageFiles(source);
        List<string> files = Directory.GetFiles(source.Path).ToList();

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(files, target.Path, replace: false);

        // Assert
        result.Installed.Should().BeTrue();

        // Act
        InstalledDictionary polish = HunspellDictionaryInstaller.List(target.Path).Single(d => d.Name == "pl_PL");

        // Assert
        polish.Problem.Should().BeNull();
        polish.Files.Select(f => (Path.GetFileName(f.Path), f.Role)).Should().BeEquivalentTo(new[]
        {
            ("pl_PL.aff", DictionaryFileRole.Required),
            ("pl_PL.dic", DictionaryFileRole.Required),
            ("hyph_pl_PL.dic", DictionaryFileRole.Hyphenation),
            ("plhyph.tex", DictionaryFileRole.Hyphenation),
            ("th_pl_PL_v2.dat", DictionaryFileRole.Thesaurus),
            ("th_pl_PL_v2.idx", DictionaryFileRole.Thesaurus),
            ("dictionaries.xcu", DictionaryFileRole.LibreOffice),
            ("README_pl_PL.txt", DictionaryFileRole.Documentation),
        });
    }

    [Fact]
    public void Installing_over_an_existing_name_needs_replace_and_then_removes_the_old_files()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom", "kot");
        WritePackageFiles(source);
        HunspellDictionaryInstaller.Install(Directory.GetFiles(source.Path), target.Path, replace: false)
            .Installed.Should().BeTrue();

        using TempDir newer = new("dict-installer-");
        DictionaryFixture.Write(newer.Path, "pl_PL", "dom", "kot", "rower");

        // Act
        DictionaryInstallResult refused = HunspellDictionaryInstaller.Install(PairIn(newer, "pl_PL"), target.Path, replace: false);

        // Assert
        refused.Installed.Should().BeFalse();
        refused.NameTaken.Should().BeTrue();
        refused.Problem.Should().Contain("pl_PL");
        File.ReadAllText(target.Combine("pl_PL.dic")).Should().NotContain("rower", "nothing changed");

        // Act
        DictionaryInstallResult replaced = HunspellDictionaryInstaller.Install(PairIn(newer, "pl_PL"), target.Path, replace: true);

        // Assert
        replaced.Installed.Should().BeTrue();
        File.ReadAllText(target.Combine("pl_PL.dic")).Should().Contain("rower");
        Directory.GetFiles(target.Path).Select(Path.GetFileName).Should().BeEquivalentTo(
            PairFiles, "a replace is not a merge — every file of the old dictionary is gone");
    }

    [Fact]
    public void Remove_deletes_every_file_of_the_dictionary_and_leaves_the_others_alone()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        WritePackageFiles(source);
        HunspellDictionaryInstaller.Install(Directory.GetFiles(source.Path), target.Path, replace: false);

        using TempDir german = new("dict-installer-");
        DictionaryFixture.Write(german.Path, "de_DE", "Haus");
        File.WriteAllText(german.Combine("hyph_de_DE.dic"), "ISO8859-1\nhau1s\n");
        HunspellDictionaryInstaller.Install(Directory.GetFiles(german.Path), target.Path, replace: false)
            .Installed.Should().BeTrue();

        // Act
        IReadOnlyList<string> toRemove = HunspellDictionaryInstaller.FilesOf(target.Path, "pl_PL");

        // Assert
        toRemove.Select(Path.GetFileName).Should().BeEquivalentTo(
            "pl_PL.aff", "pl_PL.dic", "hyph_pl_PL.dic", "th_pl_PL_v2.dat", "th_pl_PL_v2.idx", "README_pl_PL.txt");

        // Act
        HunspellDictionaryInstaller.Remove(target.Path, "pl_PL");

        // Assert
        Directory.GetFiles(target.Path).Select(Path.GetFileName).Should().BeEquivalentTo(
            "de_DE.aff", "de_DE.dic", "hyph_de_DE.dic", "dictionaries.xcu", "plhyph.tex");
        HunspellDictionaryInstaller.List(target.Path).Select(d => d.Name).Should().BeEquivalentTo("en_US", "de_DE");
    }

    [Fact]
    public void List_marks_the_built_in_dictionary_and_an_installed_one_that_overrides_it()
    {
        // Arrange
        using TempDir target = new("dict-installer-");

        // Act
        IReadOnlyList<InstalledDictionary> empty = HunspellDictionaryInstaller.List(target.Path);

        // Assert
        empty.Should().ContainSingle();
        empty[0].Name.Should().Be("en_US");
        empty[0].IsBuiltIn.Should().BeTrue();
        empty[0].OverridesBuiltIn.Should().BeFalse();
        empty[0].Files.Should().BeEmpty();
        empty[0].Problem.Should().BeNull();

        // Arrange
        using TempDir source = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "en_US", "house");

        // Act
        HunspellDictionaryInstaller.Install(PairIn(source, "en_US"), target.Path, replace: false);
        IReadOnlyList<InstalledDictionary> listed = HunspellDictionaryInstaller.List(target.Path);

        // Assert
        listed.Should().ContainSingle("an installed dictionary replaces the built-in one of the same name in the list");
        listed[0].IsBuiltIn.Should().BeFalse();
        listed[0].OverridesBuiltIn.Should().BeTrue();
        listed[0].Problem.Should().BeNull();
    }

    [Fact]
    public void A_pair_put_in_the_folder_by_hand_without_its_aff_is_listed_with_its_problem()
    {
        // Arrange
        using TempDir target = new("dict-installer-");
        DictionaryFixture.Write(target.Path, "pl_PL", "dom");
        File.Delete(target.Combine("pl_PL.aff"));

        // Act
        InstalledDictionary polish = HunspellDictionaryInstaller.List(target.Path).Single(d => d.Name == "pl_PL");

        // Assert
        polish.Problem.Should().Contain("pl_PL.aff");
        polish.Files.Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo("pl_PL.dic");
    }

    /// <summary>
    /// A dictionary in a code page encoding passes validation: its <c>.aff</c> names the encoding in its <c>SET</c>
    /// line, and the installer must load it the way <see cref="SpellChecker"/> does
    /// (<see cref="LegacyEncodings.Ensure"/>) — the regression fixed in <c>a5539d8</c>, seen from the installer's
    /// side. Without the provider the pair loads but every Polish letter is read wrong, so the dictionary does not
    /// know its own words and the content check refuses it.
    /// </summary>
    [Fact]
    public void A_dictionary_in_a_code_page_encoding_passes_validation()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        using TempDir target = new("dict-installer-");
        DictionaryFixture.WriteIso88592(source.Path, "pl_PL", "książki", "żółw", "dom");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(PairIn(source, "pl_PL"), target.Path, replace: false);

        // Assert
        result.Installed.Should().BeTrue();
        result.Problem.Should().BeNull();
        HunspellDictionaryInstaller.List(target.Path).Single(d => d.Name == "pl_PL").Problem.Should().BeNull();
    }

    [Fact]
    public void A_text_file_renamed_to_dic_is_refused_by_content()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        File.WriteAllText(source.Combine("pl_PL.dic"), "# a comment\nnot a count at all\ndom\n");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Validate(PairIn(source, "pl_PL"));

        // Assert
        result.Problem.Should().Contain("pl_PL.dic");
        result.RefusedFiles.Should().BeEmpty("the file is refused by what is inside it, not by its name");
    }

    [Fact]
    public void More_than_one_aff_at_a_time_is_refused()
    {
        // Arrange
        using TempDir source = new("dict-installer-");
        DictionaryFixture.Write(source.Path, "pl_PL", "dom");
        DictionaryFixture.Write(source.Path, "de_DE", "Haus");

        // Act
        DictionaryInstallResult result = HunspellDictionaryInstaller.Validate(Directory.GetFiles(source.Path));

        // Assert
        result.Installed.Should().BeFalse();
        result.Name.Should().BeNull();
        result.Problem.Should().Contain(".aff");
    }

    // The two files a bare dictionary is made of.
    private static readonly string[] PairFiles = { "pl_PL.aff", "pl_PL.dic" };

    // A dictionary is picked by its .aff and its .dic; the companions are chosen separately.
    private static List<string> PairIn(TempDir directory, string name) =>
        new() { directory.Combine(name + ".aff"), directory.Combine(name + ".dic") };

    // The companion files a LibreOffice dictionary package ships next to the pair; the spell checker never reads them.
    private static void WritePackageFiles(TempDir directory)
    {
        File.WriteAllText(directory.Combine("hyph_pl_PL.dic"), "ISO8859-2\ndo1m\n");
        File.WriteAllText(directory.Combine("plhyph.tex"), "\\patterns{ do1m }\n");
        File.WriteAllText(directory.Combine("th_pl_PL_v2.dat"), "ISO8859-2\ndom|1\n");
        File.WriteAllText(directory.Combine("th_pl_PL_v2.idx"), "ISO8859-2\n1\n");
        File.WriteAllText(directory.Combine("dictionaries.xcu"), "<oor:component-data />\n");
        File.WriteAllText(directory.Combine("README_pl_PL.txt"), "The Polish dictionary.\n");
    }
}
