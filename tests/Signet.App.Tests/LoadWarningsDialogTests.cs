using System;
using AwesomeAssertions;
using Signet.App.Views;
using Xunit;

namespace Signet.App.Tests;

/// <summary>List formatting in the load warnings window (<see cref="LoadWarningsDialog"/>).</summary>
public sealed class LoadWarningsDialogTests
{
    private static readonly string[] Warnings = { "  first  ", "second line one\n\nsecond line two" };

    [Fact]
    public void FormatList_bullets_each_warning_and_separates_them_with_a_blank_line()
    {
        string nl = Environment.NewLine;

        LoadWarningsDialog.FormatList(Warnings)
            .Should().Be("• first" + nl + nl + "• second line one\n\nsecond line two");
    }
}
