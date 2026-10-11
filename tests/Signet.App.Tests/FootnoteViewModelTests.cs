using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.Footnotes;
using Xunit;

namespace Signet.App.Tests;

public sealed class FootnoteViewModelTests
{
    private const string Text =
        "<html><head><title>Title</title></head><body><p>First.</p><p>Alpha <b>beta</b> &amp; gamma</p></body></html>";

    private static FootnoteViewModel New(int offset, FootnotePlacement placement = FootnotePlacement.Local) =>
        new(Text, offset, placement, 3, 12, content => FootnoteEditor.ValidateContent(content, "3.0"));

    [Fact]
    public void The_context_is_the_text_around_the_caret_without_tags_and_head()
    {
        FootnoteViewModel sut = New(Text.IndexOf("</b>", System.StringComparison.Ordinal) + 4);

        sut.ContextBefore.Should().Be("First. Alpha beta");
        sut.ContextAfter.Should().Be(" & gamma");
    }

    [Fact]
    public void A_long_context_is_shortened_next_to_the_caret()
    {
        string text = "<html><body><p>" + new string('a', 300) + new string('b', 300) + "</p></body></html>";
        FootnoteViewModel sut = new(
            text, text.IndexOf("bbb", System.StringComparison.Ordinal), FootnotePlacement.Local, 1, 1, _ => FootnoteError.None);

        sut.ContextBefore.Should().StartWith("…").And.EndWith("aaa").And.NotContain("b");
        sut.ContextAfter.Should().StartWith("bbb").And.EndWith("…").And.NotContain("a");
    }

    [Fact]
    public void The_marker_shows_the_number_of_the_chosen_placement()
    {
        FootnoteViewModel sut = New(0);
        List<string?> changed = new();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        sut.Marker.Should().Be("[3]");

        sut.IsGlobal = true;

        sut.Marker.Should().Be("[12]");
        changed.Should().Contain(nameof(FootnoteViewModel.Marker));
    }

    [Fact]
    public void An_empty_text_cannot_be_accepted_but_shows_no_error()
    {
        FootnoteViewModel sut = New(0);

        sut.Content = "   ";

        sut.CanAccept.Should().BeFalse();
        sut.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void A_text_that_is_not_well_formed_shows_the_error()
    {
        using UiCultureScope culture = new("en");
        FootnoteViewModel sut = New(0);

        sut.Content = "a < b";

        sut.CanAccept.Should().BeFalse();
        sut.ValidationError.Should().StartWith("The footnote text is not well-formed XHTML");
    }

    [Fact]
    public void A_valid_text_is_accepted_trimmed_with_the_chosen_placement()
    {
        FootnoteViewModel sut = New(0, FootnotePlacement.Global);
        sut.IsGlobal.Should().BeTrue();
        sut.IsLocal.Should().BeFalse();

        sut.Content = "  A <i>note</i>.\n";
        sut.IsLocal = true;

        sut.CanAccept.Should().BeTrue();
        sut.ValidationError.Should().BeEmpty();
        sut.Request().Should().Be(new FootnoteRequest("A <i>note</i>.", FootnotePlacement.Local));
    }

    private static FootnoteViewModel Edit(FootnotePlacement placement = FootnotePlacement.Local, bool wholeNote = false)
    {
        int start = Text.IndexOf("<b>", StringComparison.Ordinal);
        int end = Text.IndexOf("</b>", StringComparison.Ordinal) + 4;
        return FootnoteViewModel.ForEdit(
            Text, start, end, new FootnoteContent("  Old <i>text</i>.\n", wholeNote), placement, 3, 12,
            content => FootnoteEditor.ValidateContent(content, "3.0"));
    }

    [Fact]
    public void An_edited_footnote_starts_with_its_text_and_the_context_around_its_reference()
    {
        using UiCultureScope culture = new("en");
        FootnoteViewModel sut = Edit(FootnotePlacement.Global);

        sut.IsEditing.Should().BeTrue();
        sut.Content.Should().Be("  Old <i>text</i>.\n");
        sut.IsGlobal.Should().BeTrue();
        sut.Marker.Should().Be("[12]");
        sut.ContextBefore.Should().Be("First. Alpha ");
        sut.ContextAfter.Should().Be(" & gamma");
        sut.Title.Should().Be("Edit Footnote");
        sut.AcceptText.Should().Be("Save");
        sut.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void An_edited_footnote_can_be_saved_only_once_its_text_or_placement_changed()
    {
        FootnoteViewModel sut = Edit();
        sut.CanAccept.Should().BeFalse("nothing changed");

        sut.IsGlobal = true;
        sut.CanAccept.Should().BeTrue();
        sut.Request().Should().Be(new FootnoteRequest("Old <i>text</i>.", FootnotePlacement.Global));

        sut.IsLocal = true;
        sut.CanAccept.Should().BeFalse("back to the footnote as it is");

        sut.Content = "New text.";
        sut.CanAccept.Should().BeTrue();
        sut.Content = "a < b";
        sut.CanAccept.Should().BeFalse("the text is not valid");
    }

    [Fact]
    public void Removing_asks_for_the_footnote_as_it_is()
    {
        FootnoteViewModel sut = Edit(FootnotePlacement.Global);
        sut.Content = "Changed.";
        sut.IsLocal = true;

        sut.RemoveRequest().Should().Be(new FootnoteRequest("Old <i>text</i>.", FootnotePlacement.Global, Remove: true));
    }

    [Fact]
    public void The_hint_tells_when_the_text_is_the_whole_note()
    {
        using UiCultureScope culture = new("en");

        New(0).ContentHint.Should().Be(Signet.App.Resources.Strings.Get("FootnoteWindow_ContentHint"));
        Edit().ContentHint.Should().Be(Signet.App.Resources.Strings.Get("FootnoteWindow_ContentHint"));
        Edit(wholeNote: true).ContentHint.Should().Be(Signet.App.Resources.Strings.Get("FootnoteWindow_ContentHintWholeNote"));
    }
}
