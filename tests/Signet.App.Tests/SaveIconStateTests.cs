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
public sealed class SaveIconStateTests
{
    [Fact]
    public void Save_icon_is_grey_without_changes_and_red_after_editing_a_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = new();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        AppAction save = sut.Actions.Require(AppActionIds.Save);

        save.IconState.Should().Be(ActionIconState.Inactive);
        save.IsEnabled.Should().BeTrue("Save works even without changes");

        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.Document.Insert(0, "<!-- zmiana -->");

        tab.IsModified.Should().BeTrue();
        save.IconState.Should().Be(ActionIconState.Attention);
    }

    [Theory]
    [InlineData(0xEA, 0x4B, 0x4B)]
    [InlineData(0x1E, 0x88, 0xE5)]
    [InlineData(0x00, 0x00, 0x00)]
    public void Inactive_color_is_grey_and_keeps_alpha(byte r, byte g, byte b)
    {
        Color result = ActionIcons.ToInactive(Color.FromArgb(0x80, r, g, b));

        (result.R == result.G && result.G == result.B).Should().BeTrue();
        result.A.Should().Be(0x80);
    }

    [Fact]
    public void Attention_color_turns_colored_parts_red_and_keeps_white_and_grey()
    {
        Color blue = ActionIcons.ToAttention(Color.FromRgb(0x1E, 0x88, 0xE5));
        HslColor hsl = blue.ToHsl();

        (hsl.H < 1 || hsl.H > 359).Should().BeTrue("red hue");
        ActionIcons.ToAttention(Colors.White).Should().Be(Colors.White);
        ActionIcons.ToAttention(Color.FromRgb(0xD1, 0xDB, 0xE0)).Should().Be(Color.FromRgb(0xD1, 0xDB, 0xE0));
    }
}
