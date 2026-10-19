using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Several instances of the program share the settings file. Saving must keep what the other instances saved: an
/// instance that did not change a setting does not write it away, and the recent files of all of them are merged.
/// </summary>
public sealed class SettingsStoreInstancesTests
{
    private static readonly Fixture Fixture = new();

    private static string Path(string name) => $"/books/{name}-{Fixture.Create<int>()}.epub";

    [Fact]
    public void An_instance_that_did_not_touch_the_recent_files_does_not_overwrite_them()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore first = new(file);
        SettingsStore second = new(file);   // started before the first one saved anything
        string edited = Path("edited");
        first.RecentFiles = new[] { edited };

        // Act
        first.Save();

        second.ZoomText = 1.5f;             // it saves something else when it closes
        second.Save();

        // Assert
        new SettingsStore(file).RecentFiles.Should().Equal(edited);
        new SettingsStore(file).ZoomText.Should().Be(1.5f, "its own change is saved too");
    }

    [Fact]
    public void The_recent_files_of_both_instances_are_kept_with_the_last_saver_first()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        string old = Path("old");
        SettingsStore seed = new(file);
        seed.RecentFiles = new[] { old };
        seed.Save();
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string a = Path("a");
        string b = Path("b");

        first.RecentFiles = new[] { a, old };

        // Act
        first.Save();
        second.RecentFiles = new[] { b, old };
        second.Save();

        // Assert
        new SettingsStore(file).RecentFiles.Should().Equal(b, a, old);
    }

    [Fact]
    public void A_file_moved_to_the_front_in_one_instance_is_in_front_after_the_save()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        string x = Path("x");
        string y = Path("y");
        string z = Path("z");
        SettingsStore seed = new(file);
        seed.RecentFiles = new[] { x, y, z };
        seed.Save();
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string added = Path("added");
        first.RecentFiles = new[] { added, x, y, z };

        // Act
        first.Save();

        second.RecentFiles = new[] { z, x, y };     // z was opened again
        second.Save();

        // Assert
        new SettingsStore(file).RecentFiles.Should().Equal(z, added, x, y);
    }

    [Fact]
    public void A_file_removed_from_the_list_in_one_instance_is_gone()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        string keep = Path("keep");
        string gone = Path("gone");
        SettingsStore seed = new(file);
        seed.RecentFiles = new[] { keep, gone };
        seed.Save();
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string added = Path("added");
        first.RecentFiles = new[] { added, keep, gone };

        // Act
        first.Save();

        second.RecentFiles = new[] { keep };        // the missing file was dropped
        second.Save();

        // Assert
        new SettingsStore(file).RecentFiles.Should().Equal(added, keep);
    }

    [Fact]
    public void The_list_is_not_longer_than_the_limit()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string[] many = Enumerable.Range(0, SettingsStore.RecentFilesMax).Select(i => Path("m" + i)).ToArray();

        // Act
        first.RecentFiles = many;
        first.Save();

        second.RecentFiles = new[] { Path("late") };
        second.Save();

        // Assert
        new SettingsStore(file).RecentFiles.Should().HaveCount(SettingsStore.RecentFilesMax);
    }

    [Fact]
    public void Other_settings_changed_by_different_instances_are_all_kept()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string language = Fixture.Create<string>();

        // Act
        first.UiLanguage = language;
        first.Save();
        second.SpellCheck = true;
        second.Save();

        SettingsStore reloaded = new(file);

        // Assert
        reloaded.UiLanguage.Should().Be(language);
        reloaded.SpellCheck.Should().BeTrue();
    }

    [Fact]
    public void When_both_changed_the_same_setting_the_last_saver_wins()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore first = new(file);
        SettingsStore second = new(file);

        // Act
        first.ZoomText = 1.25f;
        first.Save();
        second.ZoomText = 1.75f;
        second.Save();

        // Assert
        new SettingsStore(file).ZoomText.Should().Be(1.75f);
    }

    [Fact]
    public void After_saving_an_instance_sees_what_the_others_saved()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        string a = Path("a");

        // Act
        first.RecentFiles = new[] { a };
        first.Save();

        second.Save();

        // Assert
        second.RecentFiles.Should().Equal(a);
    }

    [Fact]
    public void A_setting_removed_by_one_instance_is_removed_from_the_file()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore seed = new(file);
        seed.ZoomText = 1.5f;
        seed.Save();
        SettingsStore first = new(file);
        SettingsStore second = new(file);
        first.RecentFiles = new[] { Path("a") };
        first.Save();

        // Act
        second.SetStringMap("dock_panels", new System.Collections.Generic.Dictionary<string, string> { ["Preview"] = "0" });
        second.Save();

        // Assert
        new SettingsStore(file).ZoomText.Should().Be(1.5f, "a setting nobody changed stays");
        new SettingsStore(file).RecentFiles.Should().HaveCount(1);
    }

    [Fact]
    public void A_draft_commit_and_save_still_write_the_changes()
    {
        // Arrange
        using TempDir dir = new();
        string file = dir.Combine("settings.xml");
        SettingsStore store = new(file);
        SettingsStore draft = store.CreateDraft();
        draft.ZoomText = 1.5f;

        // Act
        store.CommitDraft(draft);
        store.Save();

        // Assert
        new SettingsStore(file).ZoomText.Should().Be(1.5f);
    }
}
