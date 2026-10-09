using System;
using System.IO;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="NavigationHistoryStore"/> — the navigation history of saved books between sessions.</summary>
public sealed class NavigationHistoryStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static NavigationPlace At(string bookPath, DateTimeOffset time) => new(bookPath, 42, 3, time);

    [Theory]
    [AutoData]
    public void A_saved_history_is_loaded_back_for_the_same_book(string back, string forward, string edited)
    {
        using TempDir temp = new();
        NavigationHistoryStore store = new(temp.Combine("history.json"), () => Now);
        string book = temp.Combine("book.epub");
        NavigationHistoryState state = new(new[] { At(back, Now) }, new[] { At(forward, Now.AddHours(-1)) }, new[] { At(edited, Now.AddMinutes(-5)) });

        store.Save(book, state, NavigationHistoryStore.MaxDays);

        NavigationHistoryState? loaded = new NavigationHistoryStore(temp.Combine("history.json"), () => Now).Load(book, NavigationHistoryStore.MaxDays);
        loaded.Should().NotBeNull();
        loaded!.Back.Should().Equal(state.Back);
        loaded.Forward.Should().Equal(state.Forward);
        loaded.Edited.Should().Equal(state.Edited);
        store.Load(temp.Combine("other.epub"), NavigationHistoryStore.MaxDays).Should().BeNull();
    }

    [Theory]
    [AutoData]
    public void Places_older_than_the_given_days_are_dropped(string recent, string old)
    {
        using TempDir temp = new();
        NavigationHistoryStore store = new(temp.Combine("history.json"), () => Now);
        string book = temp.Combine("book.epub");

        store.Save(book, new NavigationHistoryState(new[] { At(old, Now.AddDays(-3)), At(recent, Now.AddDays(-1)) }, Array.Empty<NavigationPlace>(), Array.Empty<NavigationPlace>()), 14);

        store.Load(book, 2)!.Back.Should().Equal(At(recent, Now.AddDays(-1)));
    }

    [Theory]
    [AutoData]
    public void A_book_whose_places_all_expired_is_removed_from_the_file(string place)
    {
        using TempDir temp = new();
        string file = temp.Combine("history.json");
        DateTimeOffset clock = Now;
        NavigationHistoryStore store = new(file, () => clock);
        string expired = temp.Combine("expired.epub");
        store.Save(expired, new NavigationHistoryState(new[] { At(place, Now) }, Array.Empty<NavigationPlace>(), Array.Empty<NavigationPlace>()), 1);

        clock = Now.AddDays(2);
        store.Save(temp.Combine("current.epub"), new NavigationHistoryState(new[] { At(place, clock) }, Array.Empty<NavigationPlace>(), Array.Empty<NavigationPlace>()), 1);

        File.ReadAllText(file).Should().NotContain("EXPIRED.EPUB").And.NotContain("expired.epub");
    }

    [Fact]
    public void An_unreadable_file_means_no_saved_history()
    {
        using TempDir temp = new();
        string file = temp.Combine("history.json");
        File.WriteAllText(file, "{ not json");

        new NavigationHistoryStore(file, () => Now).Load(temp.Combine("book.epub"), 14).Should().BeNull();
    }
}
