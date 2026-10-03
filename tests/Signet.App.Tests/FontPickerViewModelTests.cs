using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for the font picker window model (Preferences → "Choose…").</summary>
public sealed class FontPickerViewModelTests
{
    private static readonly FontEntry Arial = new("Arial", false);
    private static readonly FontEntry Consolas = new("Consolas", true);
    private static readonly FontEntry CourierNew = new("Courier New", true);
    private static readonly FontEntry Georgia = new("Georgia", false);

    private static readonly FontEntry[] Fonts = new[] { Arial, Consolas, CourierNew, Georgia };

    [Fact]
    public void Current_font_is_preselected_case_insensitively_taking_the_first_name_of_a_fallback_list()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("consolas, 'Courier New', monospace", 11, false));

        sut.SelectedFont.Should().Be(Consolas);
        sut.Size.Should().Be(11);
        sut.ShowSize.Should().BeTrue();
    }

    [Fact]
    public void Monospace_filter_shows_only_fixed_pitch_fonts()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Consolas", 10, true));

        sut.MonospaceOnly.Should().BeTrue();
        sut.VisibleFonts.Should().Equal(Consolas, CourierNew);
    }

    [Fact]
    public void Monospace_filter_starts_off_when_the_current_font_is_proportional()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Georgia", 10, true));

        sut.MonospaceOnly.Should().BeFalse();
        sut.SelectedFont.Should().Be(Georgia);
    }

    [Fact]
    public void Name_filter_narrows_the_list_and_drops_a_selection_that_no_longer_matches()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", null, false));

        sut.Filter = "cou";

        sut.VisibleFonts.Should().Equal(CourierNew);
        sut.SelectedFont.Should().BeNull();
        sut.CanAccept.Should().BeFalse();
        sut.Result().Should().BeNull();
    }

    [Fact]
    public void Unknown_current_font_leaves_nothing_selected()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("No Such Font", null, false));

        sut.SelectedFont.Should().BeNull();
        sut.ShowSize.Should().BeFalse();
        sut.VisibleFonts.Should().HaveCount(Fonts.Length);
    }

    [Fact]
    public void Result_returns_the_selected_family_and_a_clamped_size()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", 12, false));
        sut.SelectedFont = Georgia;
        sut.Size = 500;

        sut.Result().Should().Be(new FontPickResult("Georgia", FontPickerViewModel.MaxSize));
    }
}
