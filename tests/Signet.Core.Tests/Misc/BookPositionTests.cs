using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>The position kept per book next to the recent files: round trip, lifetime and the option.</summary>
public sealed class BookPositionTests
{
    private static BookPosition Sample() => new(
        new[] { new BookTabPosition("OEBPS/Text/a.xhtml", 120, 14), new BookTabPosition("OEBPS/Images/c.png", -1, 0) },
        "OEBPS/Text/a.xhtml");

    [Fact]
    public void A_position_survives_serialization()
    {
        // Arrange
        BookPosition position = Sample();

        // Act
        BookPosition? read = BookPosition.Parse(position.Serialize());

        // Assert
        read.Should().NotBeNull();
        read!.ActiveBookPath.Should().Be(position.ActiveBookPath);
        read.Tabs.Should().Equal(position.Tabs);
    }

    [Fact]
    public void The_window_of_a_dragged_out_tab_survives_serialization()
    {
        // Arrange
        BookPosition position = new(
            new[] { new BookTabPosition("OEBPS/Text/a.xhtml", 5, 2, new FloatingPlace(10.5, 20, 640, 480)), new BookTabPosition("b.css", 0, 1) },
            null);

        // Act
        BookPosition? read = BookPosition.Parse(position.Serialize());

        // Assert
        read!.Tabs[0].Floating.Should().Be(new FloatingPlace(10.5, 20, 640, 480));
        read.Tabs[1].Floating.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"x\":1}")]
    public void Garbage_is_not_a_position(string? text)
    {
        // Act & Assert
        BookPosition.Parse(text).Should().BeNull();
    }

    [Fact]
    public void The_position_goes_away_with_its_entry_in_the_recent_files()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        string kept = dir.Combine("kept.epub");
        string removed = dir.Combine("removed.epub");
        settings.RecentFiles = new[] { kept, removed };
        settings.SetBookPosition(kept, Sample());
        settings.SetBookPosition(removed, Sample());

        // Act
        settings.RecentFiles = new[] { kept };

        // Assert
        settings.GetBookPosition(kept).Should().NotBeNull();
        settings.GetBookPosition(removed).Should().BeNull();
    }

    [Fact]
    public void Clearing_the_recent_files_forgets_all_positions()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        string book = dir.Combine("book.epub");
        settings.RecentFiles = new[] { book };
        settings.SetBookPosition(book, Sample());

        // Act
        settings.RecentFiles = Enumerable.Empty<string>().ToList();

        // Assert
        settings.GetBookPosition(book).Should().BeNull();
    }

    [Fact]
    public void A_book_outside_the_recent_files_keeps_no_position()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        string book = dir.Combine("book.epub");

        // Act
        settings.SetBookPosition(book, Sample());

        // Assert
        settings.GetBookPosition(book).Should().BeNull();
    }

    [Fact]
    public void Each_editor_window_zooms_separately_by_default()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        SettingsStore settings = new(dir.Combine("settings.xml"));

        // Assert
        settings.ZoomEditorsSeparately.Should().BeTrue();
    }

    [Fact]
    public void Remembering_the_last_position_is_on_by_default()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        SettingsStore settings = new(dir.Combine("settings.xml"));

        // Assert
        settings.RememberLastPosition.Should().BeTrue();
    }
}
