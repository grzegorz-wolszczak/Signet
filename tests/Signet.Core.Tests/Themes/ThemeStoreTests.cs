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
    public void Signet_light_is_the_defaults_and_signet_dark_sets_every_key_that_has_one()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);

        sut.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark");
        sut.LoadProblems.Should().BeEmpty();
        Theme light = sut.Get(ThemeStore.LightId);
        Theme dark = sut.Get(ThemeStore.DarkId);
        light.Entries.Should().BeEmpty("Signet Light is the defaults of the registry");
        sut.Resolver.Variant(light).Should().Be(InterfaceVariant.Light);
        sut.Resolver.Variant(dark).Should().Be(InterfaceVariant.Dark);
        ThemeKeys.All.Where(k => k.DefaultColor is not null || k.DefaultAttributes is not null)
            .Select(k => k.Id).Should().BeSubsetOf(dark.Entries.Keys);
        sut.Resolver.Attributes(dark, ThemeKeys.EditorText).Background.Should().Be(ThemeColor.Parse("#1E1E1E"));
    }

    [Fact]
    public void A_change_to_a_bundled_theme_is_kept_beside_it_and_restore_defaults_removes_it()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme dark = sut.Get(ThemeStore.DarkId);

        sut.Save(dark.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));

        sut.IsModified(ThemeStore.DarkId).Should().BeTrue();
        Theme overlay = ThemeXml.FromXml(File.ReadAllText(dir.Combine("signet-dark.signet-theme.xml")));
        overlay.Entries.Keys.Should().Equal(ThemeKeys.XhtmlTag.Id);
        ThemeStore reopened = new(dir.Path);
        reopened.Resolver.Attributes(reopened.Get(ThemeStore.DarkId), ThemeKeys.XhtmlTag).Should().Be(Changed);
        reopened.Resolver.Attributes(reopened.Get(ThemeStore.DarkId), ThemeKeys.CssValue)
            .Should().Be(sut.Resolver.Attributes(dark, ThemeKeys.CssValue), "the rest stays bundled");

        reopened.RestoreDefaults(ThemeStore.DarkId);

        reopened.IsModified(ThemeStore.DarkId).Should().BeFalse();
        File.Exists(dir.Combine("signet-dark.signet-theme.xml")).Should().BeFalse();
        reopened.Get(ThemeStore.DarkId).Entries[ThemeKeys.XhtmlTag.Id].Should().Be(dark.Entries[ThemeKeys.XhtmlTag.Id]);
    }

    [Fact]
    public void Saving_a_bundled_theme_with_its_own_values_keeps_no_changes()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme light = sut.Get(ThemeStore.LightId);

        sut.Save(light.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(ThemeKeys.XhtmlTag.DefaultAttributes!)));

        sut.IsModified(ThemeStore.LightId).Should().BeFalse();
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ThemeStore.LightId)]
    [InlineData(ThemeStore.DarkId)]
    public void A_key_of_a_bundled_theme_is_changed_only_when_its_value_differs_from_the_bundled_one(string id)
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme bundled = sut.Get(id);
        ThemeEntry own = bundled.Entries.GetValueOrDefault(ThemeKeys.XhtmlTag.Id) ?? ThemeEntry.Of(ThemeKeys.XhtmlTag.DefaultAttributes!);

        Theme changed = bundled.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed));

        sut.ChangesKey(bundled, ThemeKeys.XhtmlTag).Should().BeFalse();
        sut.ChangesKey(bundled.With(ThemeKeys.XhtmlTag.Id, own), ThemeKeys.XhtmlTag).Should().BeFalse("it is the bundled value");
        sut.ChangesKey(changed, ThemeKeys.XhtmlTag).Should().BeTrue();
        sut.ChangesKey(changed, ThemeKeys.CssValue).Should().BeFalse();
        Theme restored = sut.WithoutChange(changed, ThemeKeys.XhtmlTag);
        sut.ChangesKey(restored, ThemeKeys.XhtmlTag).Should().BeFalse();
        sut.Resolver.Attributes(restored, ThemeKeys.XhtmlTag).Should().Be(sut.Resolver.Attributes(bundled, ThemeKeys.XhtmlTag));
    }

    [Fact]
    public void A_key_of_a_theme_of_the_user_is_changed_when_the_theme_sets_it_and_undoing_takes_the_parent_value()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme user = sut.Duplicate(ThemeStore.DarkId, Any.Create<string>());

        Theme changed = user.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed));

        sut.ChangesKey(user, ThemeKeys.XhtmlTag).Should().BeFalse();
        sut.ChangesKey(changed, ThemeKeys.XhtmlTag).Should().BeTrue();
        sut.ChangesKey(user.With(ThemeKeys.XhtmlCssComment.Id, ThemeEntry.Inherited), ThemeKeys.XhtmlCssComment)
            .Should().BeTrue("an inherit entry is the theme's own too");
        Theme restored = sut.WithoutChange(changed, ThemeKeys.XhtmlTag);
        restored.Entries.Should().NotContainKey(ThemeKeys.XhtmlTag.Id);
        sut.Resolver.Attributes(restored, ThemeKeys.XhtmlTag)
            .Should().Be(sut.Resolver.Attributes(sut.Get(ThemeStore.DarkId), ThemeKeys.XhtmlTag));
    }

    [Fact]
    public void A_copy_of_a_bundled_theme_has_it_as_the_parent_and_keeps_its_changes()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        sut.Save(sut.Get(ThemeStore.DarkId).With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));

        Theme copy = sut.Duplicate(ThemeStore.DarkId, "Signet Dark");

        copy.Name.Should().Be("Signet Dark (2)");
        copy.ParentId.Should().Be(ThemeStore.DarkId);
        copy.Entries.Keys.Should().Equal(ThemeKeys.XhtmlTag.Id);
        Ulid.TryParse(copy.Id, out _).Should().BeTrue();
        ThemeStore reopened = new(dir.Path);
        reopened.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark", "Signet Dark (2)");
        reopened.Resolver.Attributes(reopened.Get(copy.Id), ThemeKeys.XhtmlTag).Should().Be(Changed);
        reopened.Resolver.Variant(reopened.Get(copy.Id)).Should().Be(InterfaceVariant.Dark);
    }

    [Fact]
    public void Only_a_theme_of_the_user_can_be_renamed_to_a_free_name_or_deleted()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme mine = sut.Duplicate(ThemeStore.LightId, Any.Create<string>());
        string name = Any.Create<string>();

        sut.Rename(mine.Id, name).Name.Should().Be(name);

        FluentActions.Invoking(() => sut.Rename(mine.Id, "signet LIGHT")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => sut.Rename(ThemeStore.LightId, name)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => sut.Delete(ThemeStore.DarkId)).Should().Throw<InvalidOperationException>();
        sut.Delete(mine.Id);
        sut.Find(mine.Id).Should().BeNull();
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Fact]
    public void Deleting_a_parent_leaves_its_children_looking_the_same()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme parent = sut.Duplicate(ThemeStore.DarkId, "Parent");
        sut.Save(parent.With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));
        using (StringReader reader = new(ThemeXml.ToXml(new Theme(Ulid.NewUlid().ToString(), "Child") { ParentId = parent.Id })))
        {
            sut.Import(reader, new List<string>());
        }

        Theme child = sut.Themes.Single(t => t.Name == "Child");

        sut.Delete(parent.Id);

        Theme adopted = new ThemeStore(dir.Path).Get(child.Id);
        adopted.ParentId.Should().Be(ThemeStore.DarkId);
        sut.Resolver.Attributes(adopted, ThemeKeys.XhtmlTag).Should().Be(Changed);
    }

    [Fact]
    public void An_exported_bundled_theme_imports_as_a_theme_of_the_user_with_the_same_look()
    {
        using TempDir source = new();
        using TempDir target = new();
        ThemeStore from = new(source.Path);
        from.Save(from.Get(ThemeStore.DarkId).With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(Changed)));
        using StringWriter file = new();
        from.Export(ThemeStore.DarkId, file);
        ThemeStore to = new(target.Path);
        List<string> warnings = new();

        Theme imported = to.Import(new StringReader(file.ToString()), warnings);

        warnings.Should().BeEmpty();
        imported.Name.Should().Be("Signet Dark (2)");
        imported.ParentId.Should().Be(ThemeStore.DarkId);
        to.Resolver.Attributes(imported, ThemeKeys.XhtmlTag).Should().Be(Changed);
        to.IsModified(ThemeStore.DarkId).Should().BeFalse("importing does not change the bundled theme");
    }

    [Fact]
    public void An_import_gets_a_new_identifier_when_its_own_is_taken_and_signet_light_for_a_missing_parent()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        string parent = Any.Create<string>();
        string xml = ThemeXml.ToXml(new Theme(ThemeStore.DarkId, "Imported") { ParentId = parent });
        List<string> warnings = new();

        Theme imported = sut.Import(new StringReader(xml), warnings);

        imported.Id.Should().NotBe(ThemeStore.DarkId);
        imported.ParentId.Should().Be(ThemeStore.LightId);
        warnings.Should().Equal(CoreStrings.Format("Theme_ParentMissing", parent));
        sut.Get(ThemeStore.DarkId).Name.Should().Be("Signet Dark", "the bundled theme is not replaced");
    }

    [Fact]
    public void A_broken_file_is_reported_and_the_others_are_read()
    {
        using TempDir dir = new();
        ThemeStore sut = new(dir.Path);
        Theme mine = sut.Duplicate(ThemeStore.LightId, "Mine");
        File.WriteAllText(dir.Combine("broken" + ThemeXml.Extension), "<signet-theme version=\"1\" name=\"x\" />");

        ThemeStore reopened = new(dir.Path);

        reopened.Find(mine.Id).Should().NotBeNull();
        reopened.LoadProblems.Should().ContainSingle().Which.Should().StartWith("broken" + ThemeXml.Extension);
    }
}
