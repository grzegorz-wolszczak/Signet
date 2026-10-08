using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests of <see cref="DebugLog"/>: masked paths, values as they are written, language-independent labels and
/// "before → after" tracking of settings.
/// </summary>
public sealed class DebugLogTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void A_path_in_the_profile_folder_is_masked()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string file = _fixture.Create<string>() + ".epub";

        DebugLog.MaskPath(Path.Combine(home, "Books", file)).Should().Be("~" + Path.DirectorySeparatorChar + Path.Combine("Books", file));
        DebugLog.MaskPath(null).Should().BeEmpty();
    }

    [Fact]
    public void Values_are_written_quoted_invariant_and_shortened()
    {
        DebugLog.Describe(null).Should().Be("(none)");
        DebugLog.Describe("abc").Should().Be("\"abc\"");
        DebugLog.Describe(1.5).Should().Be("1.5");
        DebugLog.Describe(true).Should().Be("True");
        DebugLog.Describe(new string('x', 500)).Length.Should().BeLessThan(220);
    }

    [Theory]
    [InlineData("Common_Close")]
    [InlineData("CleanupWindow_SelectAll")]
    public void A_label_is_written_as_the_resource_key_of_its_text(string key)
    {
        string text = Strings.Get(key);

        string? found = DebugLog.ResourceKeyOf(text);

        found.Should().NotBeNull();
        Strings.Get(found!).Should().Be(text, "a key with the same text identifies the same label");
        DebugLog.ResourceKeyOf("_" + text + "…").Should().Be(found, "mnemonics and an ellipsis are ignored");
    }

    [Fact]
    public void A_text_that_is_no_resource_has_no_key()
    {
        DebugLog.ResourceKeyOf(_fixture.Create<string>()).Should().BeNull();
        DebugLog.ResourceKeyOf("  ").Should().BeNull();
    }

    [Fact]
    public void A_tracked_setting_is_logged_with_its_value_before_and_after()
    {
        bool wasEnabled = DebugLog.IsEnabled;
        DebugLog.IsEnabled = true;
        try
        {
            Settings settings = new();
            string context = "Test" + Guid.NewGuid().ToString("N");
            using (DebugLog.TrackChanges(settings, context))
            {
                settings.Flag = true;
                settings.Count = 7;
                settings.Count = 7;
            }

            settings.Flag = false;

            DebugLog.RecentEvents().Should()
                .Contain($"{context}: Flag: False → True")
                .And.Contain($"{context}: Count: 3 → 7")
                .And.NotContain($"{context}: Flag: True → False", "changes after disposing are not tracked");
        }
        finally
        {
            DebugLog.IsEnabled = wasEnabled;
        }
    }

    private sealed class Settings : INotifyPropertyChanged
    {
        private bool _flag;
        private int _count = 3;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Flag
        {
            get => _flag;
            set => Set(ref _flag, value);
        }

        public int Count
        {
            get => _count;
            set => Set(ref _count, value);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
