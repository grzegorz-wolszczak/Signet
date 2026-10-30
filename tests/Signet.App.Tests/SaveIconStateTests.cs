using System;
using System.Linq;
using Avalonia.Media;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The "Save" floppy icon on the toolbar and in the menu: grey without changes, red with unsaved
/// changes (the action works in both states).
/// </summary>
public sealed class SaveIconStateTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [Fact]
    public void Save_icon_is_grey_without_changes_and_red_after_editing_a_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        AppAction save = sut.Actions.Require(AppActionIds.Save);

        // Act & Assert
        save.IconState.Should().Be(ActionIconState.Inactive);
        save.IsEnabled.Should().BeTrue("Save works even without changes");

        // Act
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.Document.Insert(0, "<!-- zmiana -->");

        // Assert
        tab.IsModified.Should().BeTrue();
        save.IconState.Should().Be(ActionIconState.Attention);
    }

    /// <summary>
    /// Activating another tab flushes the edited one into its file: its changes become the book's, so the icon stays red
    /// (it went gray — the changes were then neither in the tab nor in the book, and closing did not ask about saving).
    /// Closing the edited tab does the same.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_icon_stays_red_when_the_edited_tab_is_left_or_closed(bool close)
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        AppAction save = sut.Actions.Require(AppActionIds.Save);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var edited = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        edited.Document.Insert(0, "<!-- change -->");
        save.IconState.Should().Be(ActionIconState.Attention);

        // Act
        if (close)
        {
            sut.Tabs.CloseActiveTab();
        }
        else
        {
            sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<CssResource>().First() });
        }

        // Assert
        edited.IsModified.Should().BeFalse("its text went into the file");
        book.Modified.Should().BeTrue("the change is the book's now");
        save.IconState.Should().Be(ActionIconState.Attention);
    }

    [Theory]
    [InlineData(0xEA, 0x4B, 0x4B)]
    [InlineData(0x1E, 0x88, 0xE5)]
    [InlineData(0x00, 0x00, 0x00)]
    public void Inactive_color_is_grey_and_keeps_alpha(byte r, byte g, byte b)
    {
        // Act
        Color result = ActionIcons.ToInactive(Color.FromArgb(0x80, r, g, b));

        // Assert
        (result.R == result.G && result.G == result.B).Should().BeTrue();
        result.A.Should().Be(0x80);
    }

    [Fact]
    public void Attention_color_turns_colored_parts_red_and_keeps_white_and_grey()
    {
        // Act
        Color blue = ActionIcons.ToAttention(Color.FromRgb(0x1E, 0x88, 0xE5));
        HslColor hsl = blue.ToHsl();

        // Assert
        (hsl.H < 1 || hsl.H > 359).Should().BeTrue("red hue");

        // Act & Assert
        ActionIcons.ToAttention(Colors.White).Should().Be(Colors.White);
        ActionIcons.ToAttention(Color.FromRgb(0xD1, 0xDB, 0xE0)).Should().Be(Color.FromRgb(0xD1, 0xDB, 0xE0));
    }
}
