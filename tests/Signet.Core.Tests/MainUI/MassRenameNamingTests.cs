using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="MassRenameNaming"/> — the "Mass Rename" logic.</summary>
public sealed class MassRenameNamingTests
{
    [Theory]
    [InlineData(2, '0', 3, "002")]
    [InlineData(12, '#', 7, "#####12")]
    [InlineData(12, '0', 1, "12")]
    [InlineData(1234, '0', 3, "1234")]
    [InlineData(0, '-', 4, "---0")]
    public void A_number_is_padded_on_the_left_to_the_minimum_length_and_never_cut(
        int number, char padding, int minLength, string expected) =>
        MassRenameNaming.FormatNumber(number, padding, minLength).Should().Be(expected);

    [Fact]
    public void The_defaults_are_a_prefix_part_from_1_without_padding_in_the_order_of_the_book()
    {
        MassRenameOptions.Default.Should().Be(new MassRenameOptions("part", 1, '0', 1, true));
    }

    [Fact]
    public void Normalizing_trims_the_prefix_and_clamps_the_numbers()
    {
        new MassRenameOptions("  ab ", -4, '0', 500, false).Normalized()
            .Should().Be(new MassRenameOptions("ab", 0, '0', MassRenameOptions.MinLengthMax, false));
        new MassRenameOptions("x", int.MaxValue, '0', 0, false).Normalized()
            .Should().Be(new MassRenameOptions("x", MassRenameOptions.StartNumberMax, '0', 1, false));
    }

    [Theory]
    [InlineData("part", '0', 3, true)]
    [InlineData("pa:rt", '0', 3, false)]
    [InlineData("part", '/', 3, false)]
    [InlineData("part", '/', 1, true)]
    [InlineData("part", '\0', 1, true)]
    [InlineData("part", '\0', 3, false)]
    [InlineData("", '0', 1, true)]
    public void The_prefix_and_the_padding_character_may_not_have_characters_a_file_name_cannot_have(
        string prefix, char padding, int minLength, bool valid)
    {
        bool result = MassRenameNaming.IsValid(new MassRenameOptions(prefix, 1, padding, minLength, false), out string? error);

        result.Should().Be(valid);
        (error is null).Should().Be(valid);
    }

    [Fact]
    public void Every_file_gets_prefix_number_and_its_own_extension()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        List<Resource> resources = new() { book.CreateEmptyHtmlFile(), book.CreateEmptyCssFile(), book.CreateEmptyHtmlFile() };
        List<string> all = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? names = MassRenameNaming.BuildFilenames(
            resources, new MassRenameOptions("part", 2, '0', 3, false), all, out string? error);

        error.Should().BeNull();
        names.Should().Equal("part002" + Ext(resources[0]), "part003.css", "part004" + Ext(resources[2]));
    }

    [Fact]
    public void A_character_and_a_length_pad_the_number()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        List<Resource> resources = new() { book.CreateEmptyHtmlFile() };
        List<string> all = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? names = MassRenameNaming.BuildFilenames(
            resources, new MassRenameOptions("part", 12, '#', 7, false), all, out _);

        names.Should().Equal("part#####12" + Ext(resources[0]));
    }

    [Fact]
    public void A_name_of_another_file_of_the_book_is_a_collision_and_nothing_is_built()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource first = book.CreateEmptyHtmlFile();
        HtmlResource second = book.CreateEmptyHtmlFile();
        string taken = "part1" + Ext(second);
        second.RenameTo(taken);
        List<string> all = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? names = MassRenameNaming.BuildFilenames(
            new List<Resource> { first }, new MassRenameOptions("part", 1, '0', 1, false), all, out string? error);

        names.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_name_a_file_already_has_is_not_a_collision()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource only = book.CreateEmptyHtmlFile();
        only.RenameTo("part1" + Ext(only));
        List<string> all = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? names = MassRenameNaming.BuildFilenames(
            new List<Resource> { only }, new MassRenameOptions("part", 1, '0', 1, false), all, out string? error);

        error.Should().BeNull();
        names.Should().Equal("part1" + Ext(only));
    }

    [Fact]
    public void A_prefix_with_a_forbidden_character_builds_nothing()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        List<Resource> resources = new() { book.CreateEmptyHtmlFile() };

        MassRenameNaming.BuildFilenames(resources, new MassRenameOptions("a/b", 1, '0', 1, false),
            new List<string>(book.GetFolderKeeper().GetAllFilenames()), out string? error).Should().BeNull();
        error.Should().Contain("/");
    }

    private static string Ext(Resource resource) => resource.Filename[resource.Filename.LastIndexOf('.')..];
}
