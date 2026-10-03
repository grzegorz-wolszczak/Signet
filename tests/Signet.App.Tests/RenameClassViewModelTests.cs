using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>View model of the "Rename Class" window: validation, scope, counter, source notes.</summary>
public sealed class RenameClassViewModelTests
{
    private const string Chapter =
        "<html><head><link rel=\"stylesheet\" href=\"s.css\"/></head>"
        + "<body><p class=\"c1\">a</p><div><p class=\"x c1\">b</p></div></body></html>";

    private static ClassRenamer Renamer() =>
        new([new ClassRenameSource("ch.xhtml", Chapter)], [new ClassRenameSource("s.css", ".c1 { }\n")]);

    private static RenameClassViewModel FromElement() => new(Renamer(), new ClassAtCaret("c1", ["html", "body", "p"]));

    private static RenameClassViewModel FromSheet() => new(Renamer(), new StyleClassAtCaret("c1", "s.css", -1));

    [Fact]
    public void Starts_with_the_old_name_and_cannot_be_accepted_until_it_changes()
    {
        RenameClassViewModel sut = FromElement();

        sut.NewName.Should().Be("c1");
        sut.CanAccept.Should().BeFalse();
        sut.ValidationMessages.Should().Equal(Strings.Get("RenameClass_SameAsOld"));

        sut.NewName = "lead";

        sut.CanAccept.Should().BeTrue();
        sut.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void Shows_every_problem_of_the_new_name()
    {
        RenameClassViewModel sut = FromElement();

        sut.NewName = "1a.b";

        sut.CanAccept.Should().BeFalse();
        sut.ValidationMessages.Should().Equal(
            Strings.Format("RenameClass_InvalidCharacters", "'.'"),
            Strings.Get("RenameClass_StartsWithDigit"));
    }

    [Fact]
    public void Rejects_a_class_that_already_exists()
    {
        RenameClassViewModel sut = FromElement();

        sut.NewName = "x";

        sut.ValidationMessages.Should().Equal(Strings.Format("RenameClass_AlreadyExists", "x"));
    }

    [Fact]
    public void Summary_and_source_notes_follow_the_chosen_scope()
    {
        RenameClassViewModel sut = FromElement();

        sut.ShowsScopeChoice.Should().BeTrue();
        sut.IsEverywhere.Should().BeTrue();
        sut.Summary.Should().Be(Strings.Format("RenameClassWindow_Summary", 2, 2, 2));
        sut.SourceNotes.Should().Equal(Strings.Format("RenameClassWindow_SourceRenamed", "s.css", 1));

        sut.IsSameNesting = true;

        sut.IsEverywhere.Should().BeFalse();
        sut.Request.Scope.Should().Be(ClassRenameScope.SameNesting);
        sut.Summary.Should().Be(Strings.Format("RenameClassWindow_Summary", 1, 2, 2));
        sut.SourceNotes.Should().Equal(Strings.Format("RenameClassWindow_SourceCopied", "s.css", 1));
        sut.SameNestingLabel.Should().Be(Strings.Format("RenameClassWindow_SameNesting", "html > body > p"));
    }

    [Fact]
    public void Started_from_a_stylesheet_has_no_scope_choice_and_names_the_source()
    {
        RenameClassViewModel sut = FromSheet();

        sut.ShowsScopeChoice.Should().BeFalse();
        sut.Request.Scope.Should().Be(ClassRenameScope.StyleSource);
        sut.ClassLabel.Should().Be(Strings.Format("RenameClassWindow_ClassInSource", "c1", "s.css"));
        sut.Summary.Should().Be(Strings.Format("RenameClassWindow_Summary", 2, 2, 2));
    }

    [Fact]
    public void Explains_when_the_changed_elements_use_several_sources()
    {
        const string html = "<html><head><link rel=\"stylesheet\" href=\"a.css\"/><link rel=\"stylesheet\" href=\"b.css\"/>"
            + "<style>.c1 { }</style></head><body><p class=\"c1\">a</p></body></html>";
        ClassRenamer renamer = new(
            [new ClassRenameSource("ch.xhtml", html),
             new ClassRenameSource("ch2.xhtml", "<html><head><link rel=\"stylesheet\" href=\"b.css\"/></head><body><p class=\"c1\">z</p></body></html>")],
            [new ClassRenameSource("a.css", ".c1 { }\n"), new ClassRenameSource("b.css", ".c1 { }\n")]);

        RenameClassViewModel sut = new(renamer, new StyleClassAtCaret("c1", "a.css", -1));

        sut.SourceNotes.Should().Equal(
            Strings.Format("RenameClassWindow_MultipleSources", 3),
            Strings.Format("RenameClassWindow_SourceRenamed", "a.css", 1),
            Strings.Format("RenameClassWindow_SourceCopied", "b.css", 1),
            Strings.Format("RenameClassWindow_SourceRenamed", Strings.Format("RenameClassWindow_StyleBlock", "ch.xhtml", 1), 1));
    }

    [Fact]
    public void Notes_that_the_class_has_no_definitions()
    {
        RenameClassViewModel sut = new(
            new ClassRenamer([new ClassRenameSource("ch.xhtml", "<html><body><p class=\"c1\"/></body></html>")], []),
            new ClassAtCaret("c1", ["html", "body", "p"]));

        sut.SourceNotes.Should().Equal(Strings.Get("RenameClassWindow_NoDefinitions"));
    }

    [Fact]
    public void Rename_uses_the_typed_name_and_scope()
    {
        RenameClassViewModel sut = FromElement();
        sut.NewName = "lead";
        sut.IsSameNesting = true;

        ClassRenameResult result = sut.Rename();

        result.ChangedTexts["ch.xhtml"].Should().Contain("<p class=\"lead\">a</p><div><p class=\"x c1\">b</p>");
        result.ChangedTexts["s.css"].Should().Be(".c1 { }\n.lead { }\n");
    }
}
