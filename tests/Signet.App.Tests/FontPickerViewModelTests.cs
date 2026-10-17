using System.Collections.Generic;
using System.Linq;
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

    private static readonly int[] ThreeWeights = { 300, 400, 700 };
    private static readonly int[] LightAndRegular = { 300, 400 };
    private static readonly int[] RegularOnly = { 400 };

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

    // The weights each font has: Arial the full range, Consolas only Regular and Bold, Georgia only Regular.
    private static int[] WeightsOf(string family) => family switch
    {
        "Arial" => new[] { 300, 400, 600, 700 },
        "Consolas" => new[] { 400, 700 },
        _ => new[] { 400 },
    };

    [Fact]
    public void Without_a_weight_in_the_request_there_is_no_list_of_weights()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", 12, false), WeightsOf);

        sut.ShowWeight.Should().BeFalse();
        sut.Weights.Should().BeEmpty();
        sut.Result()!.Weight.Should().Be(0);
    }

    [Fact]
    public void The_weights_of_the_selected_font_are_listed_with_the_default_first_and_the_current_one_selected()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", 12, false, 600), WeightsOf);

        sut.ShowWeight.Should().BeTrue();
        sut.Weights.Select(w => w.Name).Should().Equal(FontWeightOptions.Default.Name, "Light", "Regular", "SemiBold", "Bold");
        sut.SelectedWeight!.Value.Should().Be(600);
        sut.Result()!.Weight.Should().Be(600);
    }

    [Fact]
    public void Changing_the_font_changes_the_list_and_keeps_the_weight_the_new_font_has()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", 12, false, 700), WeightsOf);

        sut.SelectedFont = Consolas;

        sut.Weights.Select(w => w.Value).Should().Equal(0, 400, 700);
        sut.SelectedWeight!.Value.Should().Be(700);

        sut.SelectedFont = Georgia;

        sut.Weights.Select(w => w.Value).Should().Equal(0, 400);
        sut.SelectedWeight!.Value.Should().Be(0, "a font without the weight falls back to the default");

        sut.SelectedFont = Arial;

        sut.SelectedWeight!.Value.Should().Be(700, "the weight wanted comes back when a font has it again");
    }

    [Fact]
    public void A_stored_weight_the_font_lacks_stays_selectable()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Georgia", 12, false, 300), WeightsOf);

        sut.Weights.Select(w => w.Value).Should().Equal(0, 300, 400);
        sut.SelectedWeight!.Value.Should().Be(300);
        sut.Result()!.Weight.Should().Be(300);
    }

    [Fact]
    public void A_weight_chosen_in_the_list_is_the_result()
    {
        FontPickerViewModel sut = new(Fonts, new FontPickRequest("Arial", 12, false, 0), WeightsOf);
        sut.SelectedWeight!.Value.Should().Be(0);

        sut.SelectedWeight = sut.Weights.Single(w => w.Value == 300);

        sut.Result()!.Weight.Should().Be(300);
        sut.PreviewWeight.Should().Be(Avalonia.Media.FontWeight.Light);
    }

    [Fact]
    public void Each_change_in_the_window_is_reported_to_the_caller_but_not_the_start()
    {
        List<FontPickResult> reported = new();
        FontPickerViewModel sut = new(
            Fonts, new FontPickRequest("Arial", 12, false, 400, reported.Add), family => ThreeWeights);

        reported.Should().BeEmpty("opening the window changes nothing");

        sut.Size = 15;
        sut.SelectedWeight = sut.Weights.Single(w => w.Value == 700);
        sut.SelectedFont = Georgia;

        reported.First().Should().Be(new FontPickResult("Arial", 15, 400));
        reported.Should().Contain(new FontPickResult("Arial", 15, 700));
        reported.Last().Should().Be(new FontPickResult("Georgia", 15, 700), "the weight wanted stays while the font has it");
    }

    [Fact]
    public void A_change_of_the_font_is_reported_once_with_the_weights_of_the_new_font()
    {
        List<FontPickResult> reported = new();
        FontPickerViewModel sut = new(
            Fonts, new FontPickRequest("Arial", 12, false, 300, reported.Add), family => family == "Arial" ? LightAndRegular : RegularOnly);

        sut.SelectedFont = Georgia;

        reported.Should().Equal(new FontPickResult("Georgia", 12, 0));
    }
}
