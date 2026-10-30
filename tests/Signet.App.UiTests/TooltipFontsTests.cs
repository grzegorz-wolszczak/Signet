using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>The tooltip font of each area of the application (<see cref="TooltipFonts"/>).</summary>
public sealed class TooltipFontsTests
{
    [AvaloniaFact]
    public void A_tooltip_uses_the_font_of_the_innermost_area_and_follows_changes()
    {
        // Arrange
        Button inBookBrowser = Tipped();
        Button inMainWindow = Tipped();
        StackPanel bookBrowser = new() { Children = { inBookBrowser } };
        TooltipFonts.SetArea(bookBrowser, TooltipArea.BookBrowser);
        StackPanel main = new() { Children = { bookBrowser, inMainWindow } };
        TooltipFonts.SetArea(main, TooltipArea.MainWindow);
        Window window = new() { Content = main };
        try
        {
            // Act
            TooltipFonts.Apply(area => area switch
            {
                TooltipArea.BookBrowser => new TooltipFont("Courier New", 21),
                TooltipArea.MainWindow => new TooltipFont("Georgia", 15),
                _ => TooltipFont.Default,
            });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Assert
            TipFont(inBookBrowser).Should().Be(("Courier New", 21d));
            TipFont(inMainWindow).Should().Be(("Georgia", 15d));

            // Act
            TooltipFonts.Apply(area => area == TooltipArea.BookBrowser ? new TooltipFont("Arial", 17) : TooltipFont.Default);

            // Assert
            TipFont(inBookBrowser).Should().Be(("Arial", 17d), "a change applies to the open windows");
        }
        finally
        {
            TooltipFonts.Apply(_ => TooltipFont.Default);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_tooltip_takes_the_weight_of_its_area()
    {
        // Arrange
        Button button = Tipped();
        StackPanel area = new() { Children = { button } };
        TooltipFonts.SetArea(area, TooltipArea.Checkpoints);
        Window window = new() { Content = area };
        try
        {
            // Act
            TooltipFonts.Apply(a => a == TooltipArea.Checkpoints ? new TooltipFont("Arial", 14, 300) : TooltipFont.Default);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Assert
            TipWeight(button).Should().Be(FontWeight.Light);

            // Act
            TooltipFonts.Apply(_ => TooltipFont.Default);

            // Assert
            TipWeight(button).Should().Be(FontWeight.Normal, "a weight that is not set is Regular");
        }
        finally
        {
            TooltipFonts.Apply(_ => TooltipFont.Default);
            window.Close();
        }
    }

    /// <summary>
    /// A new application starts with no area roots: a later change does not restyle the roots of an earlier one. The
    /// headless tests create an application per test, and restyling every root left by the earlier tests made each test
    /// start slower than the one before (the UI test run took five times longer).
    /// </summary>
    [AvaloniaFact]
    public void Reset_forgets_the_area_roots_so_a_later_change_does_not_restyle_them()
    {
        // Arrange
        StackPanel area = new();
        TooltipFonts.SetArea(area, TooltipArea.BookBrowser);
        try
        {
            // Act
            TooltipFonts.Reset();
            object? before = area.Resources[TooltipFonts.SizeResourceKey];

            TooltipFonts.Apply(_ => new TooltipFont("Arial", 23));

            // Assert
            area.Resources[TooltipFonts.SizeResourceKey].Should().Be(before, "the root belongs to an earlier application");
            Application.Current!.Resources[TooltipFonts.SizeResourceKey].Should().Be(23d, "the application still follows the change");
        }
        finally
        {
            TooltipFonts.Reset();
        }
    }

    // Opens the tooltip of the control and reads the weight its text is shown in.
    private static FontWeight TipWeight(Control control)
    {
        ToolTip.SetIsOpen(control, true);
        Dispatcher.UIThread.RunJobs();
        FieldInfo field = typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)!;
        ToolTip tip = (ToolTip)control.GetValue((AvaloniaProperty)field.GetValue(null)!)!;
        FontWeight weight = tip.GetVisualDescendants().OfType<TextBlock>().First().FontWeight;
        ToolTip.SetIsOpen(control, false);
        Dispatcher.UIThread.RunJobs();
        return weight;
    }

    [AvaloniaFact]
    public void Outside_every_area_a_tooltip_uses_the_font_of_the_other_windows_in_the_interface_typeface()
    {
        // Arrange
        Button button = Tipped();
        Window window = new() { Content = button };
        try
        {
            // Act
            TooltipFonts.Apply(area => area == TooltipArea.OtherWindows ? new TooltipFont(string.Empty, 16) : TooltipFont.Default);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Act & Assert
            Application.Current!.TryGetResource(FluentResourceKeys.ContentControlThemeFontFamily, null, out object? interfaceFont).Should().BeTrue();

            // Assert
            TipFont(button).Should().Be((((FontFamily)interfaceFont!).Name, 16d));
        }
        finally
        {
            TooltipFonts.Apply(_ => TooltipFont.Default);
            window.Close();
        }
    }

    private static Button Tipped()
    {
        Button button = new() { Content = "Save" };
        ToolTip.SetTip(button, "Save the book");
        return button;
    }

    // Opens the tooltip of the control and reads the font its text is shown in.
    private static (string Family, double Size) TipFont(Control control)
    {
        ToolTip.SetIsOpen(control, true);
        Dispatcher.UIThread.RunJobs();
        // The ToolTip a control shows is kept in a non-public attached property.
        FieldInfo field = typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)!;
        ToolTip tip = (ToolTip)control.GetValue((AvaloniaProperty)field.GetValue(null)!)!;
        TextBlock text = tip.GetVisualDescendants().OfType<TextBlock>().First();
        (string, double) font = (text.FontFamily.Name, text.FontSize);
        ToolTip.SetIsOpen(control, false);
        Dispatcher.UIThread.RunJobs();
        return font;
    }
}
