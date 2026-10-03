using System.IO;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="ClipboardHistoryService"/>: the history update logic
/// (<see cref="ClipboardHistoryService.ApplyClipboardText"/>), without the real system clipboard.
/// </summary>
public sealed class ClipboardHistoryServiceTests
{
    private static SettingsStore NewSettings() => new(Path.Combine(Path.GetTempPath(), $"cliphist-{System.Guid.NewGuid():N}.json"));

    [Fact]
    public void ApplyClipboardText_NewValue_AddsToFrontAndPersists()
    {
        SettingsStore settings = NewSettings();
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText("hello");

        sut.Items.Should().Equal("hello");
        settings.ClipboardHistory.Should().Equal("hello");
    }

    [Fact]
    public void ApplyClipboardText_SecondValue_IsInsertedBeforeFirst()
    {
        SettingsStore settings = NewSettings();
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText("first");
        sut.ApplyClipboardText("second");

        sut.Items.Should().Equal("second", "first");
    }

    [Fact]
    public void ApplyClipboardText_DuplicateOfExistingEntry_MovesItToFront()
    {
        SettingsStore settings = NewSettings();
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText("a");
        sut.ApplyClipboardText("b");
        sut.ApplyClipboardText("a");

        sut.Items.Should().Equal("a", "b");
    }

    [Fact]
    public void ApplyClipboardText_SameTextTwiceInARow_IsIgnored()
    {
        SettingsStore settings = NewSettings();
        ClipboardHistoryService sut = new(settings);
        int changeCount = 0;
        sut.Changed += (_, _) => changeCount++;

        sut.ApplyClipboardText("same");
        sut.ApplyClipboardText("same");

        sut.Items.Should().Equal("same");
        changeCount.Should().Be(1);
    }

    [Fact]
    public void ApplyClipboardText_EmptyOrNull_IsIgnored()
    {
        SettingsStore settings = NewSettings();
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText(string.Empty);
        sut.ApplyClipboardText(null);

        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void ApplyClipboardText_LimitZero_DoesNotRecordAnything()
    {
        SettingsStore settings = NewSettings();
        settings.ClipboardHistoryLimit = 0;
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText("text");

        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void ApplyClipboardText_ExceedsLimit_TrimsOldestEntries()
    {
        SettingsStore settings = NewSettings();
        settings.ClipboardHistoryLimit = 2;
        ClipboardHistoryService sut = new(settings);

        sut.ApplyClipboardText("one");
        sut.ApplyClipboardText("two");
        sut.ApplyClipboardText("three");

        sut.Items.Should().Equal("three", "two");
    }

    [Fact]
    public void Constructor_LoadsExistingHistoryFromSettings()
    {
        SettingsStore settings = NewSettings();
        settings.ClipboardHistory = new[] { "old1", "old2" };

        ClipboardHistoryService sut = new(settings);

        sut.Items.Should().Equal("old1", "old2");
    }
}
