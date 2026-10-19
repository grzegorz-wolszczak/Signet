using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

public sealed class ThemeStoreTests
{
    private static readonly Fixture Any = new();
    private static readonly TextAttributes Changed = new() { Foreground = ThemeColor.Parse("#123456"), Bold = true };

    [Fact]
    public void Signet_light_is_the_defaults_and_the_dark_color_scheme_sets_every_editor_key_that_has_one()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);

        sut.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark");
        sut.LoadProblems.Should().BeEmpty();

        Theme light = sut.Get(ThemeStore.LightId);
        Theme dark = sut.Get(ThemeStore.DarkId);

        light.Entries.Should().BeEmpty("Signet Light is the defaults of the registry");

        ThemeKeys.All.Where(k => k.Document == ThemeDocumentKind.Scheme && (k.DefaultColor is not null || k.DefaultAttributes is not null))
            .Select(k => k.Id).Should().BeSubsetOf(dark.Entries.Keys);

        dark.Entries.Keys.Should().OnlyContain(id => ThemeKeys.Get(id).Document == ThemeDocumentKind.Scheme, "the interface is the theme's");

        // Act & Assert
        sut.Resolver.Attributes(dark, ThemeKeys.EditorText).Background.Should().Be(ThemeColor.Parse("#1E1E1E"));
    }

    [Fact]
    public void The_dark_theme_is_dark_controls_with_every_interface_key_that_has_a_default()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);

        // Act & Assert
        sut.Kind.Should().Be(ThemeDocumentKind.Theme);

        // Assert
        sut.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark");

        sut.LoadProblems.Should().BeEmpty();

        // Arrange
        Theme light = sut.Get(ThemeStore.LightId);
        Theme dark = sut.Get(ThemeStore.DarkId);

        // Assert
        light.Entries.Should().BeEmpty();

        // Act & Assert
        sut.Resolver.Variant(light).Should().Be(InterfaceVariant.Light);
        sut.Resolver.Variant(dark).Should().Be(InterfaceVariant.Dark);

        // Act & Assert
        ThemeKeys.All.Where(k => k.Document == ThemeDocumentKind.Theme
                && (k.DefaultColor is not null || (k.DefaultAttributes is not null && k.DefaultAttributes != TextAttributes.Empty)))
            .Select(k => k.Id).Should().BeSubsetOf(dark.Entries.Keys);

        // Act & Assert
        dark.Entries.Keys.Should().OnlyContain(id => ThemeKeys.Get(id).Document == ThemeDocumentKind.Theme, "the editors are the scheme's");
    }

    [Fact]
    public void Themes_and_color_schemes_are_kept_in_files_of_their_own_kind()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore themes = new(dir.Path);
        ThemeStore schemes = new(dir.Path, ThemeDocumentKind.Scheme);

        // Act
        Theme theme = themes.Duplicate(ThemeStore.DarkId, "Mine");

        Theme scheme = schemes.Duplicate(ThemeStore.DarkId, "Mine");

        // Assert
        Directory.GetFiles(dir.Path).Select(Path.GetFileName).Should().BeEquivalentTo(
            theme.Id + ThemeXml.Extension, scheme.Id + ThemeXml.SchemeExtension);
        new ThemeStore(dir.Path).Themes.Select(t => t.Id).Should().Contain(theme.Id).And.NotContain(scheme.Id);
        new ThemeStore(dir.Path, ThemeDocumentKind.Scheme).Themes.Select(t => t.Id).Should().Contain(scheme.Id).And.NotContain(theme.Id);
    }

    [Fact]
    public void A_color_scheme_cannot_be_imported_as_a_theme_and_the_other_way_round()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore themes = new(dir.Path);
        ThemeStore schemes = new(dir.Path, ThemeDocumentKind.Scheme);
        using StringWriter themeFile = new();
        using StringWriter schemeFile = new();

        // Act
        themes.Export(ThemeStore.DarkId, themeFile);
        schemes.Export(ThemeStore.DarkId, schemeFile);

        // Act & Assert
        FluentActions.Invoking(() => schemes.Import(new StringReader(themeFile.ToString()), new List<string>()))
            .Should().Throw<ThemeFormatException>();

        // Act & Assert
        FluentActions.Invoking(() => themes.Import(new StringReader(schemeFile.ToString()), new List<string>()))
            .Should().Throw<ThemeFormatException>();
    }

    [Fact]
    public void A_change_to_a_bundled_theme_is_kept_beside_it_and_restore_defaults_removes_it()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme dark = sut.Get(ThemeStore.DarkId);

        // Act
        sut.Save(dark.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));

        // Assert
        sut.IsModified(ThemeStore.DarkId).Should().BeTrue();
        Theme overlay = ThemeXml.FromXml(File.ReadAllText(dir.Combine("signet-dark.signet-scheme.xml")), ThemeDocumentKind.Scheme);
        overlay.Entries.Keys.Should().Equal(ThemeKeys.XhtmlTag.Id);
        ThemeStore reopened = new(dir.Path, ThemeDocumentKind.Scheme);
        reopened.Resolver.Attributes(reopened.Get(ThemeStore.DarkId), ThemeKeys.XhtmlTag).Should().Be(Changed);
        reopened.Resolver.Attributes(reopened.Get(ThemeStore.DarkId), ThemeKeys.CssValue)
            .Should().Be(sut.Resolver.Attributes(dark, ThemeKeys.CssValue), "the rest stays bundled");

        // Act
        reopened.RestoreDefaults(ThemeStore.DarkId);

        // Assert
        reopened.IsModified(ThemeStore.DarkId).Should().BeFalse();
        File.Exists(dir.Combine("signet-dark.signet-scheme.xml")).Should().BeFalse();
        reopened.Get(ThemeStore.DarkId).Entries[ThemeKeys.XhtmlTag.Id].Should().Be(dark.Entries[ThemeKeys.XhtmlTag.Id]);
    }

    [Fact]
    public void Saving_a_bundled_theme_with_its_own_values_keeps_no_changes()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme light = sut.Get(ThemeStore.LightId);

        // Act
        sut.Save(light.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(ThemeKeys.XhtmlTag.DefaultAttributes!)));

        // Assert
        sut.IsModified(ThemeStore.LightId).Should().BeFalse();
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ThemeStore.LightId)]
    [InlineData(ThemeStore.DarkId)]
    public void A_key_of_a_bundled_theme_is_changed_only_when_its_value_differs_from_the_bundled_one(string id)
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme bundled = sut.Get(id);
        ThemeEntry own = bundled.Entries.GetValueOrDefault(ThemeKeys.XhtmlTag.Id) ?? ThemeEntry.Of(ThemeKeys.XhtmlTag.DefaultAttributes!);

        Theme changed = bundled.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed));

        // Act & Assert
        sut.ChangesKey(bundled, ThemeKeys.XhtmlTag).Should().BeFalse();

        // Act & Assert
        sut.ChangesKey(bundled.With(ThemeKeys.XhtmlTag.Id, own), ThemeKeys.XhtmlTag).Should().BeFalse("it is the bundled value");

        // Act & Assert
        sut.ChangesKey(changed, ThemeKeys.XhtmlTag).Should().BeTrue();

        // Act & Assert
        sut.ChangesKey(changed, ThemeKeys.CssValue).Should().BeFalse();

        // Act
        Theme restored = sut.WithoutChange(changed, ThemeKeys.XhtmlTag);

        // Act & Assert
        sut.ChangesKey(restored, ThemeKeys.XhtmlTag).Should().BeFalse();

        // Act & Assert
        sut.Resolver.Attributes(restored, ThemeKeys.XhtmlTag).Should().Be(sut.Resolver.Attributes(bundled, ThemeKeys.XhtmlTag));
    }

    [Fact]
    public void A_key_of_a_theme_of_the_user_is_changed_when_the_theme_sets_it_and_undoing_takes_the_parent_value()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme user = sut.Duplicate(ThemeStore.DarkId, Any.Create<string>());

        Theme changed = user.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed));

        // Act & Assert
        sut.ChangesKey(user, ThemeKeys.XhtmlTag).Should().BeFalse();

        // Act & Assert
        sut.ChangesKey(changed, ThemeKeys.XhtmlTag).Should().BeTrue();

        // Act & Assert
        sut.ChangesKey(user.With(ThemeKeys.XhtmlCssComment.Id, ThemeEntry.Inherited), ThemeKeys.XhtmlCssComment)
            .Should().BeTrue("an inherit entry is the theme's own too");

        // Act
        Theme restored = sut.WithoutChange(changed, ThemeKeys.XhtmlTag);

        // Assert
        restored.Entries.Should().NotContainKey(ThemeKeys.XhtmlTag.Id);
        sut.Resolver.Attributes(restored, ThemeKeys.XhtmlTag)
            .Should().Be(sut.Resolver.Attributes(sut.Get(ThemeStore.DarkId), ThemeKeys.XhtmlTag));
    }

    [Fact]
    public void A_copy_of_a_bundled_theme_has_it_as_the_parent_and_keeps_its_changes()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        sut.Save(sut.Get(ThemeStore.DarkId).With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));

        // Act
        Theme copy = sut.Duplicate(ThemeStore.DarkId, "Signet Dark");

        // Assert
        copy.Name.Should().Be("Signet Dark (2)");
        copy.ParentId.Should().Be(ThemeStore.DarkId);
        copy.Entries.Keys.Should().Equal(ThemeKeys.XhtmlTag.Id);

        // Act & Assert
        Ulid.TryParse(copy.Id, out _).Should().BeTrue();

        // Act
        ThemeStore reopened = new(dir.Path, ThemeDocumentKind.Scheme);

        // Assert
        reopened.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark", "Signet Dark (2)");

        // Act & Assert
        reopened.Resolver.Attributes(reopened.Get(copy.Id), ThemeKeys.XhtmlTag).Should().Be(Changed);
    }

    [Fact]
    public void Only_a_theme_of_the_user_can_be_renamed_to_a_free_name_or_deleted()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme mine = sut.Duplicate(ThemeStore.LightId, Any.Create<string>());
        string name = Any.Create<string>();

        // Act & Assert
        sut.Rename(mine.Id, name).Name.Should().Be(name);

        // Act & Assert
        FluentActions.Invoking(() => sut.Rename(mine.Id, "signet LIGHT")).Should().Throw<ArgumentException>();

        // Act & Assert
        FluentActions.Invoking(() => sut.Rename(ThemeStore.LightId, name)).Should().Throw<InvalidOperationException>();

        // Act & Assert
        FluentActions.Invoking(() => sut.Delete(ThemeStore.DarkId)).Should().Throw<InvalidOperationException>();

        // Act
        sut.Delete(mine.Id);

        // Assert
        sut.Find(mine.Id).Should().BeNull();
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Fact]
    public void Deleting_a_parent_leaves_its_children_looking_the_same()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme parent = sut.Duplicate(ThemeStore.DarkId, "Parent");

        // Act
        sut.Save(parent.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));
        using (StringReader reader = new(ThemeXml.ToXml(new Theme(Ulid.NewUlid().ToString(), "Child") { ParentId = parent.Id }, ThemeDocumentKind.Scheme)))
        {
            sut.Import(reader, new List<string>());
        }

        // Arrange
        Theme child = sut.Themes.Single(t => t.Name == "Child");

        // Act
        sut.Delete(parent.Id);

        Theme adopted = new ThemeStore(dir.Path, ThemeDocumentKind.Scheme).Get(child.Id);

        // Assert
        adopted.ParentId.Should().Be(ThemeStore.DarkId);

        // Act & Assert
        sut.Resolver.Attributes(adopted, ThemeKeys.XhtmlTag).Should().Be(Changed);
    }

    [Fact]
    public void An_exported_bundled_theme_imports_as_a_theme_of_the_user_with_the_same_look()
    {
        // Arrange
        using TempDir source = new();
        using TempDir target = new();
        ThemeStore from = new(source.Path, ThemeDocumentKind.Scheme);
        from.Save(from.Get(ThemeStore.DarkId).With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));
        using StringWriter file = new();

        // Act
        from.Export(ThemeStore.DarkId, file);

        // Arrange
        ThemeStore to = new(target.Path, ThemeDocumentKind.Scheme);
        List<string> warnings = new();

        // Act
        Theme imported = to.Import(new StringReader(file.ToString()), warnings);

        // Assert
        warnings.Should().BeEmpty();
        imported.Name.Should().Be("Signet Dark (2)");
        imported.ParentId.Should().Be(ThemeStore.DarkId);
        to.Resolver.Attributes(imported, ThemeKeys.XhtmlTag).Should().Be(Changed);
        to.IsModified(ThemeStore.DarkId).Should().BeFalse("importing does not change the bundled theme");
    }

    [Fact]
    public void An_import_gets_a_new_identifier_when_its_own_is_taken_and_signet_light_for_a_missing_parent()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        string parent = Any.Create<string>();
        string xml = ThemeXml.ToXml(new Theme(ThemeStore.DarkId, "Imported") { ParentId = parent }, ThemeDocumentKind.Scheme);
        List<string> warnings = new();

        // Act
        Theme imported = sut.Import(new StringReader(xml), warnings);

        // Assert
        imported.Id.Should().NotBe(ThemeStore.DarkId);
        imported.ParentId.Should().Be(ThemeStore.LightId);
        warnings.Should().Equal(CoreStrings.Format("Theme_ParentMissing", parent));
        sut.Get(ThemeStore.DarkId).Name.Should().Be("Signet Dark", "the bundled theme is not replaced");
    }

    [Fact]
    public void A_broken_file_is_reported_and_the_others_are_read()
    {
        // Arrange
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path, ThemeDocumentKind.Scheme);
        Theme mine = sut.Duplicate(ThemeStore.LightId, "Mine");
        File.WriteAllText(dir.Combine("broken" + ThemeXml.SchemeExtension), "<signet-scheme version=\"1\" name=\"x\" />");

        // Act
        ThemeStore reopened = new(dir.Path, ThemeDocumentKind.Scheme);

        // Assert
        reopened.Find(mine.Id).Should().NotBeNull();
        reopened.LoadProblems.Should().ContainSingle().Which.Should().StartWith("broken" + ThemeXml.SchemeExtension);
    }
}
