using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The Recent Locations popup: its entries (visited / edited places), filtering, forgetting an entry, going to one,
/// and the time texts.
/// </summary>
public sealed class RecentLocationsViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDir _temp = new();
    private readonly MainWindowViewModel _sut;
    private readonly Book _book;
    private readonly List<Action> _commandEnds = new();

    public RecentLocationsViewModelTests()
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, _temp);
        _sut = new MainWindowViewModel();
        _book = new ImportEpub(epub).GetBook();
        _sut.LoadBook(_book, epub);
        _sut.NavigationCommandScheduler = action =>
        {
            _commandEnds.Add(action);
            return true;
        };
    }

    public void Dispose()
    {
        _sut.CheckpointHistory.Dispose();
        _temp.Dispose();
    }

    private HtmlResource Chapter(int number) =>
        _book.GetHtmlResources().First(h => h.Filename == $"chapter{number}.xhtml");

    private void Open(Resource resource)
    {
        _sut.Tabs.OpenResources(new[] { resource });
        foreach (Action end in _commandEnds.ToList())
        {
            end();
        }

        _commandEnds.Clear();
    }

    private RecentLocationsViewModel ShowPopup()
    {
        RecentLocationsViewModel? popup = null;
        _sut.RecentLocationsRequested += (_, model) => popup = model;
        _sut.Actions.Require(AppActionIds.RecentLocations).Execute(null);
        return popup ?? throw new InvalidOperationException("The popup was not requested.");
    }

    [Fact]
    public void The_popup_lists_the_visited_files_newest_first_with_their_text()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        Open(_book.GetAllResources().OfType<CssResource>().First());

        RecentLocationsViewModel popup = ShowPopup();

        popup.Items.Select(i => i.FileName).Take(2).Should().Equal("chapter2.xhtml", "chapter1.xhtml");
        popup.Items[0].Snippet.Text.Should().NotBeEmpty();
        popup.Selected.Should().BeSameAs(popup.Items[0]);
        popup.CountText.Should().Be($"({popup.Items.Count})");
    }

    [Fact]
    public void Edits_in_the_active_tab_appear_under_show_edited_only()
    {
        Open(Chapter(1));
        var tab = (CodeTabViewModel)_sut.Tabs.ActiveTab!;
        tab.Document.Insert(tab.Document.Text.IndexOf("</body>", StringComparison.Ordinal), "<p>new text</p>");

        RecentLocationsViewModel popup = ShowPopup();
        popup.ShowEditedOnly = true;

        popup.Title.Should().Be(Strings.Get("RecentLocations_TitleEdited"));
        popup.Items.Should().ContainSingle().Which.Snippet.Text.Should().Contain("new text");
    }

    [Fact]
    public void Typing_filters_and_Delete_forgets_the_selected_place()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        Open(_book.GetAllResources().OfType<CssResource>().First());
        RecentLocationsViewModel popup = ShowPopup();

        popup.Filter = "chapter1";
        popup.Items.Should().ContainSingle().Which.FileName.Should().Be("chapter1.xhtml");

        popup.RemoveSelected();

        popup.Items.Should().BeEmpty();
        _sut.NavigationHistory.BackPlaces.Should().NotContain(p => p.BookPath == Chapter(1).BookPath);
    }

    [Fact]
    public void Choosing_an_entry_closes_the_popup_and_goes_to_the_place()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        RecentLocationsViewModel popup = ShowPopup();
        bool closed = false;
        popup.CloseRequested += (_, _) => closed = true;

        popup.Navigate(popup.Items.Single(i => i.FileName == "chapter1.xhtml"));

        closed.Should().BeTrue();
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(1));
    }

    [Fact]
    public void MoveSelection_stays_within_the_list()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        Open(_book.GetAllResources().OfType<CssResource>().First());
        RecentLocationsViewModel popup = ShowPopup();

        popup.MoveSelection(-1);
        popup.Selected.Should().BeSameAs(popup.Items[0]);

        popup.MoveSelection(100);
        popup.Selected.Should().BeSameAs(popup.Items[^1]);
    }

    [Theory]
    [InlineData(0, "Przed chwilą")]
    [InlineData(1, "Minutę temu")]
    [InlineData(3, "3 minuty temu")]
    [InlineData(5, "5 minut temu")]
    [InlineData(12, "12 minut temu")]
    [InlineData(22, "22 minuty temu")]
    [InlineData(60, "Godzinę temu")]
    public void FormatTime_within_an_hour_uses_minutes_with_polish_plural_forms(int minutes, string expected)
    {
        using UiCultureScope culture = new("pl");

        RecentLocationsViewModel.FormatTime(Now.AddMinutes(-minutes), Now).Should().Be(expected);
    }

    [Fact]
    public void FormatTime_of_older_places_names_the_day()
    {
        using UiCultureScope culture = new("en");
        DateTimeOffset now = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 9)));

        RecentLocationsViewModel.FormatTime(now.AddHours(-5), now).Should().StartWith("Today ");
        RecentLocationsViewModel.FormatTime(now.AddDays(-1), now).Should().StartWith("Yesterday ");
        RecentLocationsViewModel.FormatTime(now.AddDays(-5), now).Should().NotStartWith("Today").And.NotStartWith("Yesterday");
    }
}
