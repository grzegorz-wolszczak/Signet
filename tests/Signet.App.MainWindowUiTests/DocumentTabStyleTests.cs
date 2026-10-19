using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Themes;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.MainWindowUiTests;

/// <summary>The look of the document tabs: the colors of the theme, the font of the setting, the rows when they do not fit.</summary>
public sealed class DocumentTabStyleTests
{
    private sealed class Scenario : IDisposable
    {
        private readonly TempDir _temp = new();
        private readonly Book _book;
        public readonly MainWindow Window;
        public readonly MainWindowViewModel Vm;

        public Scenario(double width = 1200)
        {
            _book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, _temp)).GetBook();
            Window = new MainWindow { Width = width, DataContext = Vm = new MainWindowViewModel() };
            Window.Show();
            Settle();
            Vm.LoadBook(_book, string.Empty);
            foreach (TextResource resource in _book.GetAllResources().OfType<TextResource>())
            {
                Vm.NavigateToBookPathAtOffset(resource.BookPath, 0);
            }

            Settle();
        }

        public List<DocumentTabStripItem> Items => Window.GetVisualDescendants().OfType<DocumentTabStripItem>().ToList();

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            ((IBookWorkspace)Vm).CloseBook();
            Window.Close();
            _temp.Dispose();
        }
    }

    [AvaloniaFact]
    public void The_tabs_take_the_colors_of_the_theme_selected_and_not()
    {
        // Arrange
        using Scenario s = new();
        Theme theme = new Theme("t", "T")
            .With(ThemeKeys.UiDocumentTab.Id, ThemeEntry.Of(new TextAttributes
            {
                Foreground = ThemeColor.Parse("#112233"),
                Background = ThemeColor.Parse("#FFEEDD"),
            }))
            .With(ThemeKeys.UiDocumentTabSelected.Id, ThemeEntry.Of(new TextAttributes
            {
                Foreground = ThemeColor.Parse("#FFFFFF"),
                Background = ThemeColor.Parse("#AA0000"),
            }));
        try
        {
            // Act
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Of(ThemeDocumentKind.Theme, theme, new ThemeResolver(_ => null)));
            s.Settle();

            List<DocumentTabStripItem> items = s.Items;

            // Assert
            items.Should().HaveCountGreaterThan(1);
            DocumentTabStripItem selected = items.Single(i => i.IsSelected);
            ((ISolidColorBrush)selected.Foreground!).Color.Should().Be(Color.Parse("#FFFFFF"));
            ((ISolidColorBrush)selected.Background!).Color.Should().Be(Color.Parse("#AA0000"));
            foreach (DocumentTabStripItem other in items.Where(i => !i.IsSelected))
            {
                ((ISolidColorBrush)other.Foreground!).Color.Should().Be(Color.Parse("#112233"));
                ((ISolidColorBrush)other.Background!).Color.Should().Be(Color.Parse("#FFEEDD"));
            }
        }
        finally
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Defaults);
        }
    }

    [AvaloniaFact]
    public void The_tabs_take_the_font_of_the_setting_and_return_to_the_default_font()
    {
        // Arrange
        using Scenario s = new();
        double normal = s.Items[0].FontSize;
        try
        {
            // Act
            UiDensityManager.ApplyDocumentTab(Avalonia.Application.Current!, new DocumentTabFontAppearance("Courier New", 21));
            s.Settle();

            // Assert
            s.Items.Should().OnlyContain(i => i.FontSize == 21 && i.FontFamily.Name == "Courier New");
        }
        finally
        {
            // Act
            UiDensityManager.ApplyDocumentTab(Avalonia.Application.Current!, DocumentTabFontAppearance.Default);
        }

        s.Settle();

        // Assert
        s.Items.Should().OnlyContain(i => i.FontSize == normal);
    }

    [AvaloniaFact]
    public void Tabs_that_do_not_fit_go_to_several_rows_when_the_option_is_on()
    {
        // Arrange
        using Scenario s = new(width: 420);
        int Rows() => s.Items.Select(i => Math.Round(i.TranslatePoint(default, s.Window)!.Value.Y)).Distinct().Count();

        s.Items.Should().HaveCountGreaterThan(3);

        // Act & Assert
        Rows().Should().Be(1, "the tabs scroll in one row by default");

        // Act
        s.Window.Classes.Set("multilineTabs", true);
        s.Settle();

        // Assert
        Rows().Should().BeGreaterThan(1, "the tabs that do not fit start a new row");
        s.Window.GetVisualDescendants().OfType<RepeatButton>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)
            .Where(b => b.FindAncestorOfType<DocumentTabStrip>() is not null).Should().BeEmpty("there is nothing to scroll");

        // Act
        s.Window.Classes.Set("multilineTabs", false);
        s.Settle();

        // Assert
        Rows().Should().Be(1);
    }
}
