using AwesomeAssertions;
using System;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests.Models
{
    public class ColumnListTests
    {
        [AvaloniaFact(Timeout = 10000)]
        public void Columns_Are_Sized_At_End_Of_Measure()
        {
            var target = new ColumnList<Model>
            {
                new TextColumn<Model, string?>(null, x => x.Name, new GridLength(100, GridUnitType.Pixel)),
                new TextColumn<Model, string?>(null, x => x.Name, GridLength.Auto),
                new TextColumn<Model, string?>(null, x => x.Name, new GridLength(1, GridUnitType.Star)),
                new TextColumn<Model, string?>(null, x => x.Name, new GridLength(3, GridUnitType.Star)),
            };

            target.ViewportChanged(new Rect(0, 0, 500, 500));

            for (var row = 0; row < 10; ++row)
            {
                for (var col = 0; col < target.Count; ++col)
                {
                    target.CellMeasured(col, row, new Size(51 + row, 10));
                }
            }

            target.CommitActualWidths();

            target[0].ActualWidth.Should().Be(100);
            target[1].ActualWidth.Should().Be(60);
            target[2].ActualWidth.Should().Be(85);
            target[3].ActualWidth.Should().Be(255);
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Layout_Is_Invalidated_At_End_Of_Measure_If_AutoSized_Column_Changes_Width()
        {
            var target = new ColumnList<Model>
            {
                new TextColumn<Model, string?>(null, x => x.Name, GridLength.Auto),
                new TextColumn<Model, string?>(null, x => x.Country, GridLength.Auto),
            };


            target.ViewportChanged(new Rect(0, 0, 500, 500));

            for (var row = 0; row < 10; ++row)
            {
                for (var col = 0; col < target.Count; ++col)
                {
                    target.CellMeasured(col, row, new Size(40, 10));
                }
            }

            target.CommitActualWidths();

            target.CellMeasured(0, 1, new Size(50, 10));

            var raised = 0;
            target.LayoutInvalidated += (s, e) => ++raised;

            target.CommitActualWidths();

            raised.Should().Be(1);
        }

        private class Model
        {
            public string? Name { get; set; }
            public string? Country { get; set; }
        }
    }
}
