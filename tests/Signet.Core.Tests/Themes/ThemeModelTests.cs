using System;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

public sealed class ThemeModelTests
{
    private static readonly Fixture Any = new();

    [Theory]
    [InlineData("#0645ad", 255, 0x06, 0x45, 0xAD, "#0645AD")]
    [InlineData("#80E53935", 0x80, 0xE5, 0x39, 0x35, "#80E53935")]
    [InlineData("#FF123456", 255, 0x12, 0x34, 0x56, "#123456")]
    public void A_color_is_read_from_rgb_or_argb_hex_and_written_back_short_when_opaque(
        string text, int a, int r, int g, int b, string written)
    {
        // Act
        ThemeColor color = ThemeColor.Parse(text);

        // Assert
        color.Should().Be(new ThemeColor((byte)a, (byte)r, (byte)g, (byte)b));
        color.ToString().Should().Be(written);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    [InlineData("#1234567")]
    [InlineData("red")]
    public void Anything_else_is_not_a_color(string? text)
    {
        // Act & Assert
        ThemeColor.TryParse(text, out _).Should().BeFalse();

        // Act & Assert
        FluentActions.Invoking(() => ThemeColor.Parse(text!)).Should().Throw<Exception>();
    }

    [Fact]
    public void The_registry_lists_every_key_once_grouped_by_page()
    {
        // Act & Assert
        ThemeKeys.All.Select(k => k.Id).Should().OnlyHaveUniqueItems();

        // Act & Assert
        ThemeKeys.All.Select(k => k.Page).Should().BeInAscendingOrder();

        // Act & Assert
        ThemeKeys.Get("xhtml.tag").Should().BeSameAs(ThemeKeys.XhtmlTag);

        // Act & Assert
        ThemeKeys.TryGet(Any.Create<string>(), out _).Should().BeFalse();

        // Act & Assert
        ThemeKeys.On(ThemePage.Diff).Select(k => k.Id).Should().Equal(
            "diff.deletedLine", "diff.insertedLine", "diff.emptyCell", "diff.deletedWord", "diff.insertedWord");
    }

    [Fact]
    public void Every_base_is_a_key_of_the_same_kind()
    {
        // Act & Assert
        foreach (ThemeKey key in ThemeKeys.All.Where(k => k.BaseId is not null))
        {
            ThemeKeys.Get(key.BaseId!).Kind.Should().Be(key.Kind, key.Id);
        }
    }

    [Fact]
    public void A_theme_without_entries_has_the_defaults_and_the_light_variant()
    {
        // Arrange
        Theme theme = new(Any.Create<string>(), Any.Create<string>());
        ThemeResolver sut = Resolver(theme);

        // Act & Assert
        sut.Attributes(theme, ThemeKeys.XhtmlTag).Should().Be(ThemeKeys.XhtmlTag.DefaultAttributes);

        // Act & Assert
        sut.Color(theme, ThemeKeys.EditorCurrentLine).Should().Be(ThemeKeys.EditorCurrentLine.DefaultColor);

        // Act & Assert
        sut.Color(theme, ThemeKeys.UiAccent).Should().BeNull("the system accent");

        // Act & Assert
        sut.Variant(theme).Should().Be(InterfaceVariant.Light);
    }

    [Fact]
    public void A_theme_of_the_user_takes_from_its_parent_what_it_does_not_set()
    {
        // Arrange
        TextAttributes tag = Fg("#112233");
        TextAttributes comment = Fg("#445566");
        Theme parent = new Theme("parent", Any.Create<string>()) { Variant = InterfaceVariant.Dark }
            .With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(tag))
            .With(ThemeKeys.XhtmlComment.Id, ThemeEntry.Of(comment));
        TextAttributes ownTag = Fg("#778899");
        Theme child = new Theme("child", Any.Create<string>()) { ParentId = parent.Id }
            .With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(ownTag));
        ThemeResolver sut = Resolver(parent, child);

        // Act & Assert
        sut.Attributes(child, ThemeKeys.XhtmlTag).Should().Be(ownTag);

        // Act & Assert
        sut.Attributes(child, ThemeKeys.XhtmlComment).Should().Be(comment);

        // Act & Assert
        sut.Owner(child, ThemeKeys.XhtmlComment).Should().BeSameAs(parent);

        // Act & Assert
        sut.Owner(child, ThemeKeys.XhtmlTag).Should().BeSameAs(child);

        // Act & Assert
        sut.Owner(child, ThemeKeys.CssValue).Should().BeNull();

        // Act & Assert
        sut.Variant(child).Should().Be(InterfaceVariant.Dark, "the parent's");
    }

    [Fact]
    public void An_inherited_key_takes_its_base_as_resolved_in_the_theme_asked_about()
    {
        // Arrange
        Theme parent = new Theme("parent", Any.Create<string>())
            .With(ThemeKeys.CssComment.Id, ThemeEntry.Of(Fg("#111111")))
            .With(ThemeKeys.XhtmlCssComment.Id, ThemeEntry.Inherited);
        TextAttributes changedBase = Fg("#222222");
        Theme child = new Theme("child", Any.Create<string>()) { ParentId = parent.Id }
            .With(ThemeKeys.CssComment.Id, ThemeEntry.Of(changedBase));
        ThemeResolver sut = Resolver(parent, child);

        // Act & Assert
        sut.Attributes(parent, ThemeKeys.XhtmlCssComment).Should().Be(Fg("#111111"));

        // Act & Assert
        sut.Attributes(child, ThemeKeys.XhtmlCssComment).Should().Be(changedBase,
            "the parent says \"inherit\", and the base is the one the child changed");
    }

    [Fact]
    public void A_key_no_theme_sets_keeps_its_default_and_takes_its_base_only_when_told_to_inherit()
    {
        // Arrange
        Theme theme = new Theme(Any.Create<string>(), Any.Create<string>()).With(ThemeKeys.CssValue.Id, ThemeEntry.Of(Fg("#123456")));
        ThemeResolver sut = Resolver(theme);

        // Act & Assert
        sut.Attributes(theme, ThemeKeys.XhtmlCss).Should().Be(ThemeKeys.XhtmlCss.DefaultAttributes);

        // Act & Assert
        sut.Attributes(theme.With(ThemeKeys.XhtmlCss.Id, ThemeEntry.Inherited), ThemeKeys.XhtmlCss).Should().Be(Fg("#123456"));
    }

    [Fact]
    public void Without_a_theme_every_key_with_a_base_shows_its_own_default()
    {
        // Arrange
        Theme light = new(Any.Create<string>(), Any.Create<string>());
        ThemeResolver sut = Resolver(light);

        // Act & Assert
        foreach (ThemeKey key in ThemeKeys.All.Where(k => k.BaseId is not null))
        {
            sut.Attributes(light, key).Should().Be(key.DefaultAttributes, key.Id);
        }
    }

    [Fact]
    public void A_cycle_of_parents_or_a_missing_parent_ends_the_lineage()
    {
        // Arrange
        Theme a = new("a", Any.Create<string>()) { ParentId = "b" };
        Theme b = new("b", Any.Create<string>()) { ParentId = "a" };
        Theme orphan = new("orphan", Any.Create<string>()) { ParentId = Any.Create<string>() };
        ThemeResolver sut = Resolver(a, b, orphan);

        // Act & Assert
        sut.Lineage(a).Select(t => t.Id).Should().Equal("a", "b");

        // Act & Assert
        sut.Attributes(a, ThemeKeys.XhtmlTag).Should().Be(ThemeKeys.XhtmlTag.DefaultAttributes);

        // Act & Assert
        sut.Lineage(orphan).Should().ContainSingle();
    }

    [Fact]
    public void A_key_is_read_as_its_own_kind_only()
    {
        // Arrange
        Theme theme = new(Any.Create<string>(), Any.Create<string>());
        ThemeResolver sut = Resolver(theme);

        // Act & Assert
        FluentActions.Invoking(() => sut.Color(theme, ThemeKeys.XhtmlTag)).Should().Throw<ArgumentException>();

        // Act & Assert
        FluentActions.Invoking(() => sut.Attributes(theme, ThemeKeys.EditorCurrentLine)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Without_removes_the_own_value_so_the_parent_counts_again()
    {
        // Arrange
        Theme parent = new Theme("parent", Any.Create<string>()).With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Fg("#010101")));
        Theme child = new Theme("child", Any.Create<string>()) { ParentId = parent.Id }
            .With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Fg("#020202")));
        ThemeResolver sut = Resolver(parent, child);

        // Act & Assert
        sut.Attributes(child.Without(ThemeKeys.XhtmlTag.Id), ThemeKeys.XhtmlTag).Should().Be(Fg("#010101"));
    }

    private static ThemeResolver Resolver(params Theme[] themes) =>
        new(id => themes.FirstOrDefault(t => t.Id == id));

    private static TextAttributes Fg(string color) => new() { Foreground = ThemeColor.Parse(color) };
}
