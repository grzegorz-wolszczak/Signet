using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of <see cref="RangeObservableCollection{T}"/> — range changes with a single collection event.</summary>
public sealed class RangeObservableCollectionTests
{
    private readonly Fixture _fixture = new();

    private static List<NotifyCollectionChangedEventArgs> Record(RangeObservableCollection<string> collection)
    {
        List<NotifyCollectionChangedEventArgs> events = new();
        collection.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    [Fact]
    public void InsertRange_inserts_the_items_with_one_Add_event()
    {
        string[] initial = _fixture.CreateMany<string>(3).ToArray();
        string[] added = _fixture.CreateMany<string>(4).ToArray();
        RangeObservableCollection<string> collection = new();
        collection.ResetTo(initial);
        List<NotifyCollectionChangedEventArgs> events = Record(collection);

        collection.InsertRange(1, added);

        collection.Should().Equal(initial.Take(1).Concat(added).Concat(initial.Skip(1)));
        NotifyCollectionChangedEventArgs e = events.Should().ContainSingle().Subject;
        (e.Action, e.NewStartingIndex).Should().Be((NotifyCollectionChangedAction.Add, 1));
        e.NewItems!.Cast<string>().Should().Equal(added);
    }

    [Fact]
    public void RemoveRange_removes_the_items_with_one_Remove_event()
    {
        string[] initial = _fixture.CreateMany<string>(6).ToArray();
        RangeObservableCollection<string> collection = new();
        collection.ResetTo(initial);
        List<NotifyCollectionChangedEventArgs> events = Record(collection);

        collection.RemoveRange(2, 3);

        collection.Should().Equal(initial[0], initial[1], initial[5]);
        NotifyCollectionChangedEventArgs e = events.Should().ContainSingle().Subject;
        (e.Action, e.OldStartingIndex).Should().Be((NotifyCollectionChangedAction.Remove, 2));
        e.OldItems!.Cast<string>().Should().Equal(initial[2], initial[3], initial[4]);
    }

    [Fact]
    public void ResetTo_replaces_the_content_with_one_Reset_event_and_empty_ranges_raise_nothing()
    {
        RangeObservableCollection<string> collection = new();
        collection.ResetTo(_fixture.CreateMany<string>(2));
        string[] replacement = _fixture.CreateMany<string>(5).ToArray();
        List<NotifyCollectionChangedEventArgs> events = Record(collection);

        collection.ResetTo(replacement);
        collection.InsertRange(0, System.Array.Empty<string>());
        collection.RemoveRange(0, 0);

        collection.Should().Equal(replacement);
        events.Should().ContainSingle().Which.Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }
}
