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
        Button inBookBrowser = Tipped();
        Button inMainWindow = Tipped();
        StackPanel bookBrowser = new() { Children = { inBookBrowser } };
        TooltipFonts.SetArea(bookBrowser, TooltipArea.BookBrowser);
        StackPanel main = new() { Children = { bookBrowser, inMainWindow } };
        TooltipFonts.SetArea(main, TooltipArea.MainWindow);
        Window window = new() { Content = main };
        try
        {
            TooltipFonts.Apply(area => area switch
            {
                TooltipArea.BookBrowser => new TooltipFont("Courier New", 21),
                TooltipArea.MainWindow => new TooltipFont("Georgia", 15),
                _ => TooltipFont.Default,
            });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TipFont(inBookBrowser).Should().Be(("Courier New", 21d));
            TipFont(inMainWindow).Should().Be(("Georgia", 15d));

            TooltipFonts.Apply(area => area == TooltipArea.BookBrowser ? new TooltipFont("Arial", 17) : TooltipFont.Default);

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
        Button button = Tipped();
        StackPanel area = new() { Children = { button } };
        TooltipFonts.SetArea(area, TooltipArea.Checkpoints);
        Window window = new() { Content = area };
        try
        {
            TooltipFonts.Apply(a => a == TooltipArea.Checkpoints ? new TooltipFont("Arial", 14, 300) : TooltipFont.Default);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TipWeight(button).Should().Be(FontWeight.Light);

            TooltipFonts.Apply(_ => TooltipFont.Default);

            TipWeight(button).Should().Be(FontWeight.Normal, "a weight that is not set is Regular");
        }
        finally
        {
            TooltipFonts.Apply(_ => TooltipFont.Default);
            window.Close();
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
        Button button = Tipped();
        Window window = new() { Content = button };
        try
        {
            TooltipFonts.Apply(area => area == TooltipArea.OtherWindows ? new TooltipFont(string.Empty, 16) : TooltipFont.Default);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Application.Current!.TryGetResource("ContentControlThemeFontFamily", null, out object? interfaceFont).Should().BeTrue();

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
