using System.Collections.Generic;
using Avalonia;
using AwesomeAssertions;
using Signet.App.Toolbars;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ToolbarDropResolver"/>: pointer position over a toolbar area → drop target.</summary>
public sealed class ToolbarDropResolverTests
{
    private static readonly Rect Area = new(0, 0, 400, 100);

    // Two visible rows of height 30 at y = 10 and y = 50 (layout rows 0 and 2 — row 1 is hidden):
    // row 0: File [0..100), Edit [100..200); row 2: Format [0..80).
    private static readonly List<ToolbarRowSlot> Rows = new()
    {
        new ToolbarRowSlot(0, new Rect(0, 10, 400, 30), new[]
        {
            new ToolbarSlot(ToolbarId.File, new Rect(0, 10, 100, 30)),
            new ToolbarSlot(ToolbarId.Edit, new Rect(100, 10, 100, 30)),
        }),
        new ToolbarRowSlot(2, new Rect(0, 50, 400, 30), new[]
        {
            new ToolbarSlot(ToolbarId.Format, new Rect(0, 50, 80, 30)),
        }),
    };

    [Fact]
    public void An_area_without_rows_takes_the_drop_as_a_new_row_at_the_end_of_the_layout()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(new List<ToolbarRowSlot>(), 0, Area, new Point(5, 5));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(0);
        sut.Indicator.Should().Be(Area);
    }

    [Fact]
    public void The_middle_of_a_row_drops_before_the_toolbar_whose_middle_is_to_the_right()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(120, 25));

        // Assert
        sut.NewRow.Should().BeFalse();
        sut.RowIndex.Should().Be(0);
        sut.Before.Should().Be(ToolbarId.Edit);
        sut.Indicator.X.Should().BeApproximately(98.5, 0.01);
    }

    [Fact]
    public void Left_half_of_the_first_toolbar_drops_in_front_of_it()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(10, 25));

        // Assert
        sut.Before.Should().Be(ToolbarId.File);
        sut.RowIndex.Should().Be(0);
    }

    [Fact]
    public void Right_of_the_last_toolbar_drops_at_the_end_of_the_row()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(300, 25));

        // Assert
        sut.NewRow.Should().BeFalse();
        sut.Before.Should().BeNull();
        sut.RowIndex.Should().Be(0);
        sut.Indicator.X.Should().BeApproximately(198.5, 0.01);
    }

    [Fact]
    public void The_layout_row_index_is_used_not_the_position_among_the_shown_rows()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(300, 65));

        // Assert
        sut.NewRow.Should().BeFalse();
        sut.RowIndex.Should().Be(2);
    }

    [Fact]
    public void Above_the_first_row_means_a_new_row_before_it()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(50, 3));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(0);
        sut.Indicator.Y.Should().BeApproximately(8.5, 0.01);
    }

    [Fact]
    public void The_top_edge_of_a_row_means_a_new_row_before_it()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(50, 52));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(2);
    }

    [Fact]
    public void The_bottom_edge_of_a_row_means_a_new_row_after_it()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(50, 38));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(1);
        sut.Indicator.Width.Should().Be(Area.Width);
    }

    [Fact]
    public void The_gap_between_rows_means_a_new_row_between_them()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(50, 45));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(2);
        sut.Indicator.Y.Should().BeApproximately(43.5, 0.01);
    }

    [Fact]
    public void Below_the_last_row_means_a_new_row_after_it()
    {
        // Act
        ToolbarDropTarget sut = ToolbarDropResolver.Resolve(Rows, 3, Area, new Point(50, 95));

        // Assert
        sut.NewRow.Should().BeTrue();
        sut.RowIndex.Should().Be(3);
    }
}
