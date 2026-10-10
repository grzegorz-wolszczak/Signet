using System.Collections.Generic;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.Footnotes;
using Xunit;

namespace Signet.App.Tests;

public sealed class AddFootnoteViewModelTests
{
    private const string Text =
        "<html><head><title>Title</title></head><body><p>First.</p><p>Alpha <b>beta</b> &amp; gamma</p></body></html>";

    private static AddFootnoteViewModel New(int offset, FootnotePlacement placement = FootnotePlacement.Local) =>
        new(Text, offset, placement, 3, 12, content => FootnoteEditor.ValidateContent(content, "3.0"));

    [Fact]
    public void The_context_is_the_text_around_the_caret_without_tags_and_head()
    {
        AddFootnoteViewModel sut = New(Text.IndexOf("</b>", System.StringComparison.Ordinal) + 4);

        sut.ContextBefore.Should().Be("First. Alpha beta");
        sut.ContextAfter.Should().Be(" & gamma");
    }

    [Fact]
    public void A_long_context_is_shortened_next_to_the_caret()
    {
        string text = "<html><body><p>" + new string('a', 300) + new string('b', 300) + "</p></body></html>";
        AddFootnoteViewModel sut = new(
            text, text.IndexOf("bbb", System.StringComparison.Ordinal), FootnotePlacement.Local, 1, 1, _ => FootnoteError.None);

        sut.ContextBefore.Should().StartWith("…").And.EndWith("aaa").And.NotContain("b");
        sut.ContextAfter.Should().StartWith("bbb").And.EndWith("…").And.NotContain("a");
    }

    [Fact]
    public void The_marker_shows_the_number_of_the_chosen_placement()
    {
        AddFootnoteViewModel sut = New(0);
        List<string?> changed = new();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        sut.Marker.Should().Be("[3]");

        sut.IsGlobal = true;

        sut.Marker.Should().Be("[12]");
        changed.Should().Contain(nameof(AddFootnoteViewModel.Marker));
    }

    [Fact]
    public void An_empty_text_cannot_be_accepted_but_shows_no_error()
    {
        AddFootnoteViewModel sut = New(0);

        sut.Content = "   ";

        sut.CanAccept.Should().BeFalse();
        sut.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void A_text_that_is_not_well_formed_shows_the_error()
    {
        using UiCultureScope culture = new("en");
        AddFootnoteViewModel sut = New(0);

        sut.Content = "a < b";

        sut.CanAccept.Should().BeFalse();
        sut.ValidationError.Should().StartWith("The footnote text is not well-formed XHTML");
    }

    [Fact]
    public void A_valid_text_is_accepted_trimmed_with_the_chosen_placement()
    {
        AddFootnoteViewModel sut = New(0, FootnotePlacement.Global);
        sut.IsGlobal.Should().BeTrue();
        sut.IsLocal.Should().BeFalse();

        sut.Content = "  A <i>note</i>.\n";
        sut.IsLocal = true;

        sut.CanAccept.Should().BeTrue();
        sut.ValidationError.Should().BeEmpty();
        sut.Request().Should().Be(new AddFootnoteRequest("A <i>note</i>.", FootnotePlacement.Local));
    }
}
