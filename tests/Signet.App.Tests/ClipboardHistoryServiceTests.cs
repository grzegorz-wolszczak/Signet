using System;
using System.IO;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="ClipboardHistoryService"/>: the history update logic
/// (<see cref="ClipboardHistoryService.ApplyClipboardText"/>), without the real system clipboard, and that the history
/// lives for the session only.
/// </summary>
public sealed class ClipboardHistoryServiceTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private string NewSettingsPath() => _disposables.Add(new TempDir("cliphist-")).Combine("settings.xml");

    private SettingsStore NewSettings() => new(NewSettingsPath());

    [Fact]
    public void ApplyClipboardText_NewValue_AddsToFront()
    {
        // Arrange
        ClipboardHistoryService sut = new(NewSettings());

        // Act
        sut.ApplyClipboardText("hello");

        // Assert
        sut.Items.Should().Equal("hello");
    }

    [Fact]
    public void ApplyClipboardText_SecondValue_IsInsertedBeforeFirst()
    {
        // Arrange
        ClipboardHistoryService sut = new(NewSettings());

        // Act
        sut.ApplyClipboardText("first");
        sut.ApplyClipboardText("second");

        // Assert
        sut.Items.Should().Equal("second", "first");
    }

    [Fact]
    public void ApplyClipboardText_DuplicateOfExistingEntry_MovesItToFront()
    {
        // Arrange
        ClipboardHistoryService sut = new(NewSettings());

        // Act
        sut.ApplyClipboardText("a");
        sut.ApplyClipboardText("b");
        sut.ApplyClipboardText("a");

        // Assert
        sut.Items.Should().Equal("a", "b");
    }

    [Fact]
    public void ApplyClipboardText_SameTextTwiceInARow_IsIgnored()
    {
        // Arrange
        ClipboardHistoryService sut = new(NewSettings());
        int changeCount = 0;
        sut.Changed += (_, _) => changeCount++;

        // Act
        sut.ApplyClipboardText("same");
        sut.ApplyClipboardText("same");

        // Assert
        sut.Items.Should().Equal("same");
        changeCount.Should().Be(1);
    }

    [Fact]
    public void ApplyClipboardText_EmptyOrNull_IsIgnored()
    {
        // Arrange
        ClipboardHistoryService sut = new(NewSettings());

        // Act
        sut.ApplyClipboardText(string.Empty);
        sut.ApplyClipboardText(null);

        // Assert
        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void ApplyClipboardText_LimitZero_DoesNotRecordAnything()
    {
        // Arrange
        SettingsStore settings = NewSettings();
        settings.ClipboardHistoryLimit = 0;
        ClipboardHistoryService sut = new(settings);

        // Act
        sut.ApplyClipboardText("text");

        // Assert
        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void ApplyClipboardText_ExceedsLimit_TrimsOldestEntries()
    {
        // Arrange
        SettingsStore settings = NewSettings();
        settings.ClipboardHistoryLimit = 2;
        ClipboardHistoryService sut = new(settings);

        // Act
        sut.ApplyClipboardText("one");
        sut.ApplyClipboardText("two");
        sut.ApplyClipboardText("three");

        // Assert
        sut.Items.Should().Equal("three", "two");
    }

    /// <summary>What was copied is not written to the settings, also when they are saved for another reason.</summary>
    [Fact]
    public void The_history_is_not_saved_with_the_settings()
    {
        // Arrange
        string path = NewSettingsPath();
        SettingsStore settings = new(path);
        ClipboardHistoryService sut = new(settings);

        // Act
        sut.ApplyClipboardText("copied secret");
        settings.Save();

        // Assert
        File.ReadAllText(path).Should().NotContain("copied secret");
    }

    /// <summary>A new session starts with an empty history, also over the settings a session wrote before.</summary>
    [Fact]
    public void A_new_session_starts_with_an_empty_history()
    {
        // Arrange
        string path = NewSettingsPath();
        SettingsStore first = new(path);
        new ClipboardHistoryService(first).ApplyClipboardText("from the last session");
        first.Save();

        // Act
        ClipboardHistoryService next = new(new SettingsStore(path));

        // Assert
        next.Items.Should().BeEmpty();
    }
}
