using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the "Saved Searches" panel (<see cref="SearchEditorViewModel"/>): CRUD, persistence,
/// batch Replace All / Count All, import, "Load Search" into Find &amp; Replace.
/// </summary>
public sealed class SearchEditorTests : IDisposable
{
    private readonly Disposables _disposables = new();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"signet-searcheditor-{Guid.NewGuid():N}");

    private readonly Book _book = BookCreator.CreateNewBook("2.0");

    public SearchEditorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _disposables.Dispose();
        _book.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    private static readonly string[] Seq = { "seq" };

    private SavedSearchStore Store() => new(Path.Combine(_dir, "searches.xml"));

    private FindReplaceViewModel NewFindReplace(FakeMultiFileSearchHost host) => new(
        new SettingsStore(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.xml")),
        new StatusBarService(),
        () => null,
        host);

    private SettingsStore NewSettings() => new(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.xml"));

    private HtmlResource Html(string text)
    {
        HtmlResource html = _book.CreateEmptyHtmlFile();
        html.SetText(text);
        return html;
    }

    [Fact]
    public void AddEntriesAndGroups_ThenSave_PersistsAcrossReload()
    {
        // Arrange
        SavedSearchStore store = Store();
        var host = new FakeMultiFileSearchHost();
        var panel = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(host), NewFindReplace(new FakeMultiFileSearchHost()));

        // Act
        panel.AddGroupCommand.Execute(null);
        SearchEntryNodeViewModel group = panel.Nodes.Single();
        group.Name = "Typografia";
        panel.SetSelectedNodes(new[] { group });

        // Add Search opens the Add/Edit view (Name + the embedded Find & Replace panel) instead of adding a
        // blank row directly; Done saves it under the selected group.
        panel.AddEntryCommand.Execute(null);
        panel.EditingName = "Cudzysłowy";
        panel.FindReplace.FindText = "\"";
        panel.FindReplace.ReplaceText = "„";
        panel.DoneEditCommand.Execute(null);

        panel.SaveCommand.Execute(null);

        // "Restart": a new panel instance on the same file.
        var reloaded = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(new FakeMultiFileSearchHost()), NewFindReplace(new FakeMultiFileSearchHost()));
        SearchEntryNodeViewModel reloadedEntry = reloaded.Nodes.Single().Children.Single();

        // Assert
        reloadedEntry.Name.Should().Be("Cudzysłowy");
        reloadedEntry.Find.Should().Be("\"");
        reloadedEntry.Replace.Should().Be("„");
        reloadedEntry.Controls.Should().Be("NL WR DN CF", "the Add/Edit view's Find & Replace panel starts at its defaults (Normal, Down, Current File, Wrap)");
    }

    [Fact]
    public async Task ReplaceAll_FromListOfThreeSearches_RunsThemSequentially()
    {
        // Arrange
        HtmlResource a = Html("<p>alpha beta gamma</p>");
        HtmlResource b = Html("<p>alpha alpha</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };

        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, Seq, "one", "alpha", "ALPHA", "NL DN AH"),
            new(false, Seq, "two", "beta", "BETA", "NL DN AH"),
            new(false, Seq, "three", "gamma", "GAMMA", "NL DN AH"),
        });

        var panel = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(host), NewFindReplace(new FakeMultiFileSearchHost()));
        panel.SetSelectedNodes(new[] { panel.Nodes.Single() }); // group "seq" → all 3 entries

        // Act
        await panel.ReplaceAllCommand.ExecuteAsync(null);

        // Assert
        a.GetText().Should().Be("<p>ALPHA BETA GAMMA</p>");
        b.GetText().Should().Be("<p>ALPHA ALPHA</p>");
        panel.Message.Should().Contain("5"); // 3 (alpha) + 1 (beta) + 1 (gamma)
    }

    [Theory]
    [InlineData("quote fix", false, new[] { "Fix smart quotes" })]
    [InlineData("QUOTES  smart", false, new[] { "Fix smart quotes" })]
    [InlineData("fix", false, new[] { "Fix smart quotes", "Fix dashes" })]
    [InlineData("fix nbsp", false, new string[0])]
    [InlineData("fix nbsp", true, new[] { "Fix dashes" })]
    public void Filter_matches_entries_that_contain_every_keyword_in_any_order(string filter, bool filterAll, string[] expected)
    {
        // Arrange
        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, Array.Empty<string>(), "Fix smart quotes", "\"", "“", "NL DN AH"),
            new(false, Array.Empty<string>(), "Fix dashes", "--", "&nbsp;—", "NL DN AH"),
            new(false, Array.Empty<string>(), "Remove spans", "<span>", "", "NL DN AH"),
        });
        var panel = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(new FakeMultiFileSearchHost()), NewFindReplace(new FakeMultiFileSearchHost()));

        // Act
        panel.FilterAll = filterAll;
        panel.FilterText = filter;

        // Assert
        panel.Nodes.Where(n => n.IsVisible).Select(n => n.Name).Should().Equal(expected);
    }

    [Fact]
    public void ImportFile_Xml_AddsEntriesUnderImportedGroup()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        SavedSearchStore store = Store();
        var panel = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(new FakeMultiFileSearchHost()), NewFindReplace(new FakeMultiFileSearchHost()));

        string importPath = Path.Combine(_dir, "external.xml");
        File.WriteAllText(importPath, SavedSearchIo.WriteXml(new SearchEntry[]
        {
            new(false, Array.Empty<string>(), "Foo", "x", "y", "NL DN CF"),
        }));

        // Act
        panel.ImportFile(importPath);

        SearchEntryNodeViewModel imported = panel.Nodes.Single();

        // Assert
        imported.Name.Should().Be("Imported");
        imported.Children.Single().Name.Should().Be("Foo");
    }

    [Fact]
    public void LoadSearch_PushesEntryIntoFindReplacePanel()
    {
        // Arrange
        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, Array.Empty<string>(), "regexy", @"\d+", "N", "RX WR DN AH"),
        });

        var host = new FakeMultiFileSearchHost { BookLoaded = true };
        FindReplaceViewModel findReplace = NewFindReplace(host);
        var panel = new SearchEditorViewModel(store, NewSettings(), findReplace, NewFindReplace(new FakeMultiFileSearchHost()));
        panel.SetSelectedNodes(new[] { panel.Nodes.Single() });

        // Act
        panel.LoadSearchCommand.Execute(null);

        // Assert
        findReplace.FindText.Should().Be(@"\d+");
        findReplace.ReplaceText.Should().Be("N");
        findReplace.ModeIndex.Should().Be(2); // Regex
        findReplace.LookWhereIndex.Should().Be((int)LookWhere.AllHtmlFiles);
        findReplace.OptionWrap.Should().BeTrue();
    }

    // Regression: the Add/Edit view used to share the real Find & Replace panel (so typing there — or even just
    // opening Edit Search on an entry — immediately changed what was typed in the real panel, with no "Load"
    // needed). It now has its own instance over the same book; only Load Search and Save Current Search touch the
    // real one.
    [Fact]
    public void Editing_a_search_does_not_change_the_real_Find_and_Replace_panel()
    {
        // Arrange
        SavedSearchStore store = Store();
        FindReplaceViewModel findReplace = NewFindReplace(new FakeMultiFileSearchHost());
        findReplace.FindText = "untouched";
        var panel = new SearchEditorViewModel(store, NewSettings(), findReplace, NewFindReplace(new FakeMultiFileSearchHost()));

        // Act
        panel.AddEntryCommand.Execute(null);
        panel.EditingName = "New search";
        panel.FindReplace.FindText = "typed-while-editing";

        // Assert
        findReplace.FindText.Should().Be("untouched");
        panel.FindReplace.FindText.Should().Be("typed-while-editing");

        // Act: saving it does not touch the real panel either.
        panel.DoneEditCommand.Execute(null);

        // Assert
        findReplace.FindText.Should().Be("untouched");
    }

    [Fact]
    public void MoveRight_ThenSave_KeepsEntryInsideGroup()
    {
        // Arrange
        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(true, Array.Empty<string>(), "G", "", "", ""),
            new(false, Array.Empty<string>(), "loose", "a", "b", "NL DN CF"),
        });

        var panel = new SearchEditorViewModel(store, NewSettings(), NewFindReplace(new FakeMultiFileSearchHost()), NewFindReplace(new FakeMultiFileSearchHost()));
        SearchEntryNodeViewModel loose = panel.Nodes.Single(n => n.Name == "loose");
        panel.SetSelectedNodes(new[] { loose });

        // Act
        panel.MoveRightCommand.Execute(null);
        panel.SaveCommand.Execute(null);

        // Assert
        store.Load().Select(e => e.FullName).Should().Contain("G/loose");
    }
}
