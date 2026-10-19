using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Mass Rename" window: its fields, the live preview of the names and the options it hands back.</summary>
public sealed class MassRenameWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Render(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private (BookBrowserViewModel Vm, System.Collections.Generic.List<OpfModelEntry> Text, Book Book) Open(TempDir temp, SettingsStore settings)
    {
        Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        BookBrowserViewModel vm = new(settings, new StatusBarService());
        vm.SetBook(book);
        return (vm, vm.Nodes.Single(n => n.Header == "Text").Children.Select(n => n.Entry!).ToList(), book);
    }

    [AvaloniaFact]
    public void The_window_starts_with_the_last_values_and_shows_how_the_names_come_out()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        SettingsStore settings = new(temp.Combine("settings.xml")) { MassRename = new MassRenameOptions("part", 2, '0', 3, true) };
        (BookBrowserViewModel vm, var text, Book book) = Open(temp, settings);
        using Book _ = book;

        // Act
        MassRenameWindow window = MassRenameWindow.Create(vm, text);
        window.Show();
        Render(window);

        // Assert
        window.FindControl<TextBox>("PrefixBox")!.Text.Should().Be("part");
        window.FindControl<NumericUpDown>("StartNumberBox")!.Value.Should().Be(2);
        window.FindControl<TextBox>("PaddingCharBox")!.Text.Should().Be("0");
        window.FindControl<NumericUpDown>("MinLengthBox")!.Value.Should().Be(3);
        window.FindControl<CheckBox>("BookOrderBox")!.IsChecked.Should().BeTrue();
        window.FindControl<TextBlock>("DescriptionText")!.Text.Should().Contain("part002.html").And.Contain("part#####12.html");
        window.FindControl<TextBlock>("ExampleText")!.Text.Should().Be(Strings.Format("MassRenameWindow_Example", "part002" + System.IO.Path.GetExtension(text[0].Resource.Filename)));
        window.FindControl<Button>("OkButton")!.IsEnabled.Should().BeTrue();
        window.FindControl<ItemsControl>("PreviewList")!.ItemCount.Should().Be(text.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Editing_the_fields_changes_the_preview_and_a_bad_character_blocks_the_button()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (BookBrowserViewModel vm, var text, Book book) = Open(temp, new SettingsStore(temp.Combine("settings.xml")));
        using Book _ = book;

        // Act
        MassRenameWindow window = MassRenameWindow.Create(vm, text);
        window.Show();
        Render(window);
        TextBlock example = window.FindControl<TextBlock>("ExampleText")!;
        string ext = System.IO.Path.GetExtension(text[0].Resource.Filename);

        window.FindControl<TextBox>("PrefixBox")!.Text = "x";
        window.FindControl<NumericUpDown>("StartNumberBox")!.Value = 12;
        window.FindControl<TextBox>("PaddingCharBox")!.Text = "#";
        window.FindControl<NumericUpDown>("MinLengthBox")!.Value = 7;
        Render(window);

        // Assert
        example.Text.Should().Be(Strings.Format("MassRenameWindow_Example", "x#####12" + ext));
        window.Options.Should().Be(new MassRenameOptions("x", 12, '#', 7, window.Options.BookOrder));

        // Act
        window.FindControl<TextBox>("PaddingCharBox")!.Text = "/";
        Render(window);

        // Assert
        window.FindControl<Button>("OkButton")!.IsEnabled.Should().BeFalse("a file name cannot have a slash");
        window.FindControl<TextBlock>("ErrorText")!.IsVisible.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void The_order_of_the_book_is_offered_only_for_html_files()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (BookBrowserViewModel vm, var text, Book book) = Open(temp, new SettingsStore(temp.Combine("settings.xml")));
        using Book _ = book;
        OpfModelEntry css = vm.Nodes.Single(n => n.Header == "Styles").Children.First().Entry!;

        // Act
        MassRenameWindow html = MassRenameWindow.Create(vm, text);
        MassRenameWindow mixed = MassRenameWindow.Create(vm, new[] { text[0], css });

        // Assert
        html.FindControl<CheckBox>("BookOrderBox")!.IsEnabled.Should().BeTrue();
        mixed.FindControl<CheckBox>("BookOrderBox")!.IsEnabled.Should().BeFalse();
        mixed.FindControl<CheckBox>("BookOrderBox")!.IsChecked.Should().BeFalse();
    }
}
