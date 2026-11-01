using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Rendering of the modal "Dictionary manager": the table of the installed dictionaries, the files of the selected
/// one, and the Delete button, which the built-in dictionary leaves disabled.
/// </summary>
public sealed class DictionaryManagerWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [AvaloniaFact]
    public void The_manager_lists_the_dictionaries_and_the_files_of_the_selected_one()
    {
        // Arrange
        TempDir root = _disposables.Add(new TempDir("dict-manager-ui-"));
        string hunspell = root.Combine("hunspell");
        SettingsStore settings = new(root.Combine("settings.xml"));
        SpellChecker spellChecker = new(settings, hunspell, root.Combine("user"));
        DictionaryFixture.Write(hunspell, "pl_PL", "dom", "kot");
        File.WriteAllText(Path.Combine(hunspell, "hyph_pl_PL.dic"), "ISO8859-2\ndo1m\n");
        spellChecker.Reload();

        DictionaryManagerViewModel vm = new(spellChecker, settings);
        DictionaryManagerWindow window = DictionaryManagerWindow.Create(vm);

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TreeDataGrid dictionaries = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "DictionariesGrid");
        TreeDataGrid files = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(g => g.Name == "FilesGrid");
        Button delete = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeleteButton");

        // Assert
        dictionaries.Rows!.Count.Should().Be(2);
        dictionaries.RowSelection!.SelectedItem.Should().Be(vm.SelectedDictionary);
        vm.SelectedDictionary!.IsBuiltIn.Should().BeTrue("the built-in dictionary comes first and is selected");
        files.Rows!.Count.Should().Be(0);
        delete.IsEffectivelyEnabled.Should().BeFalse("the built-in dictionary cannot be deleted");
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DirectoryText")
            .Text.Should().Contain(hunspell);

        // Act
        vm.SelectedDictionary = vm.Dictionaries.Single(d => d.Name == "pl_PL");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        // Assert
        dictionaries.RowSelection.SelectedItem.Should().Be(vm.SelectedDictionary);
        files.Rows!.Count.Should().Be(3);
        vm.Files.Select(f => f.FileName).Should().BeEquivalentTo("pl_PL.aff", "pl_PL.dic", "hyph_pl_PL.dic");
        delete.IsEffectivelyEnabled.Should().BeTrue();

        window.Close();
    }
}
