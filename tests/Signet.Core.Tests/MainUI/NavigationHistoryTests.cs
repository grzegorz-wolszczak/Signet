using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="NavigationHistory"/> — the Navigate Back / Forward stacks.</summary>
public sealed class NavigationHistoryTests
{
    private static NavigationPlace At(string bookPath, int line) => new(bookPath, line * 10, line, DateTimeOffset.UnixEpoch);

    private static bool AllExist(string bookPath) => true;

    [Theory]
    [AutoData]
    public void Back_returns_the_pushed_places_newest_first_and_Forward_comes_back(string a, string b)
    {
        NavigationHistory history = new();
        history.Push(At(a, 1));
        history.Push(At(b, 1));
        NavigationPlace current = At(b, 50);

        NavigationPlace? first = history.Back(current, AllExist);
        NavigationPlace? second = history.Back(first, AllExist);
        NavigationPlace? forward = history.Forward(second, AllExist);

        first.Should().Be(At(b, 1));
        second.Should().Be(At(a, 1));
        forward.Should().Be(At(b, 1));
        history.CanGoForward.Should().BeTrue("the place Back started from is still ahead");
        history.Forward(forward, AllExist).Should().Be(current);
    }

    [Theory]
    [AutoData]
    public void A_place_close_to_the_previous_one_in_the_same_file_replaces_it(string a)
    {
        NavigationHistory history = new();
        history.Push(At(a, 10));

        history.Push(At(a, 10 + NavigationHistory.MergeLineDistance - 1));
        history.BackPlaces.Should().ContainSingle().Which.Line.Should().Be(10 + NavigationHistory.MergeLineDistance - 1);

        history.Push(At(a, 100));
        history.BackPlaces.Should().HaveCount(2);
    }

    [Theory]
    [AutoData]
    public void A_new_jump_clears_the_forward_stack(string a, string b, string c)
    {
        NavigationHistory history = new();
        history.Push(At(a, 1));
        history.Back(At(b, 1), AllExist);

        history.Push(At(c, 1));

        history.CanGoForward.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Back_skips_and_drops_places_in_files_that_no_longer_exist(string kept, string deleted)
    {
        NavigationHistory history = new();
        history.Push(At(kept, 1));
        history.Push(At(deleted, 1));

        NavigationPlace? target = history.Back(At(kept, 80), p => p != deleted);

        target.Should().Be(At(kept, 1));
        history.BackPlaces.Should().BeEmpty();
    }

    [Theory]
    [AutoData]
    public void Back_skips_a_place_that_is_the_same_as_the_current_one(string a, string b)
    {
        NavigationHistory history = new();
        history.Push(At(a, 1));
        history.Push(At(b, 20));

        history.Back(At(b, 21), AllExist).Should().Be(At(a, 1));
    }

    [Theory]
    [AutoData]
    public void Back_without_places_returns_null_and_keeps_the_forward_stack(string a)
    {
        NavigationHistory history = new();

        history.Back(At(a, 1), AllExist).Should().BeNull();
        history.CanGoForward.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Places_without_a_caret_are_the_same_place_for_one_file(string image, string chapter)
    {
        NavigationPlace imagePlace = new(image, -1, 0, DateTimeOffset.UnixEpoch);

        NavigationHistory.IsSame(imagePlace, imagePlace with { Time = DateTimeOffset.Now }).Should().BeTrue();
        NavigationHistory.IsSame(imagePlace, At(chapter, 1)).Should().BeFalse();
    }

    [Fact]
    public void The_stacks_keep_at_most_the_limit_of_places()
    {
        NavigationHistory history = new();

        foreach (int i in Enumerable.Range(0, NavigationHistory.Limit + 5))
        {
            history.Push(At($"f{i}.xhtml", 1));
        }

        history.BackPlaces.Should().HaveCount(NavigationHistory.Limit);
        history.BackPlaces[0].BookPath.Should().Be("f5.xhtml");
    }

    [Theory]
    [AutoData]
    public void RenameBookPath_follows_the_renamed_file(string oldPath, string newPath, string other)
    {
        NavigationHistory history = new();
        history.Push(At(oldPath, 1));
        history.Push(At(other, 1));
        history.Back(At(oldPath, 50), AllExist);

        history.RenameBookPath(oldPath, newPath);

        history.BackPlaces.Should().ContainSingle().Which.BookPath.Should().Be(newPath);
        history.ForwardPlaces.Should().ContainSingle().Which.BookPath.Should().Be(newPath);
    }

    [Theory]
    [AutoData]
    public void ApplyTextChange_moves_later_places_and_collapses_removed_ones(string file, string other)
    {
        NavigationHistory history = new();
        history.Push(new NavigationPlace(file, 5, 1, DateTimeOffset.UnixEpoch));
        history.Push(new NavigationPlace(other, 50, 3, DateTimeOffset.UnixEpoch));
        history.Push(new NavigationPlace(file, 20, 3, DateTimeOffset.UnixEpoch));
        history.Push(new NavigationPlace(file, 100, 9, DateTimeOffset.UnixEpoch));

        // Replace [15, 25) (no line breaks) with "a\nb\nc" (two line breaks) — the line of offset 15 is 2.
        history.ApplyTextChange(file, 15, 2, new string('x', 10), "a\nb\nc");

        history.BackPlaces.Select(p => (p.BookPath, p.Offset, p.Line)).Should().Equal(
            (file, 5, 1),
            (other, 50, 3),
            (file, 15, 2),
            (file, 95, 11));
    }

    [Theory]
    [AutoData]
    public void Restore_brings_back_a_captured_state(string a, string b)
    {
        NavigationHistory source = new();
        source.Push(At(a, 1));
        source.Push(At(b, 1));
        source.Back(At(a, 60), AllExist);

        NavigationHistory restored = new();
        restored.Restore(source.Capture());

        restored.BackPlaces.Should().Equal(source.BackPlaces);
        restored.ForwardPlaces.Should().Equal(source.ForwardPlaces);
    }
}
