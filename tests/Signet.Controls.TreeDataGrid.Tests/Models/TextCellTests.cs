using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Signet.Controls.TreeDataGrid.Models;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests.Models
{
    public class TextCellTests
    {
        [AvaloniaFact(Timeout = 10000)]
        public void Value_Is_Initially_Read_From_String()
        {
            var binding = new BehaviorSubject<BindingValue<string>>("initial");
            var target = new TextCell<string>(binding, true);

            target.Text.Should().Be("initial");
            target.Value.Should().Be("initial");
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Modified_Value_Is_Written_To_Binding()
        {
            var binding = new BehaviorSubject<BindingValue<string>>("initial");
            var target = new TextCell<string>(binding, false);
            var result = new List<string>();

            binding.Subscribe(x => result.Add(x.Value));
            target.Value = "new";

            result.Should().Equal(new[] { "initial", "new" });
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Modified_Text_Is_Written_To_Binding()
        {
            var binding = new BehaviorSubject<BindingValue<string>>("initial");
            var target = new TextCell<string>(binding, false);
            var result = new List<string>();

            binding.Subscribe(x => result.Add(x.Value));
            target.Text = "new";

            result.Should().Equal(new[] { "initial", "new" });
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Modified_Value_Is_Written_To_Binding_On_EndEdit()
        {
            var binding = new BehaviorSubject<BindingValue<string>>("initial");
            var target = new TextCell<string>(binding, false);
            var result = new List<string>();

            binding.Subscribe(x => result.Add(x.Value));

            target.BeginEdit();
            target.Text = "new";

            target.Text.Should().Be("new");
            target.Value.Should().Be("initial");
            result.Should().Equal(new[] { "initial"});

            target.EndEdit();

            target.Text.Should().Be("new");
            target.Value.Should().Be("new");
            result.Should().Equal(new[] { "initial", "new" });
        }

        [AvaloniaFact(Timeout = 10000)]
        public void Modified_Value_Is_Not_Written_To_Binding_On_CancelEdit()
        {
            var binding = new BehaviorSubject<BindingValue<string>>("initial");
            var target = new TextCell<string>(binding, false);
            var result = new List<string>();

            binding.Subscribe(x => result.Add(x.Value));

            target.BeginEdit();
            target.Text = "new";

            target.Text.Should().Be("new");
            target.Value.Should().Be("initial");
            result.Should().Equal(new[] { "initial" });

            target.CancelEdit();

            target.Text.Should().Be("initial");
            target.Value.Should().Be("initial");
            result.Should().Equal(new[] { "initial" });
        }

        public class StringFormat
        {
            [AvaloniaFact(Timeout = 10000)]
            public void Initial_Int_Value_Is_Formatted()
            {
                var binding = new BehaviorSubject<BindingValue<int>>(42);
                var target = new TextCell<int>(binding, true, GetOptions());

                target.Text.Should().Be("42.00");
                target.Value.Should().Be(42);
            }

            [AvaloniaFact(Timeout = 10000)]
            public void Int_Value_Is_Formatted_After_Editing()
            {
                var binding = new BehaviorSubject<BindingValue<int>>(42);
                var target = new TextCell<int>(binding, false, GetOptions());
                var result = new List<int>();

                binding.Subscribe(x => result.Add(x.Value));

                target.BeginEdit();
                target.Text = "43";

                target.Text.Should().Be("43");
                target.Value.Should().Be(42);
                result.Should().Equal(new[] { 42 });

                target.EndEdit();

                target.Text.Should().Be("43.00");
                target.Value.Should().Be(43);
                result.Should().Equal(new[] { 42, 43 });
            }

            // The culture is explicit: the default is the current culture, so "42.00" became "42,00" on a Polish system.
            private ITextCellOptions? GetOptions(string format = "{0:n2}")
            {
                return new TextColumnOptions<int> { StringFormat = format, Culture = CultureInfo.InvariantCulture };
            }
        }
    }
}
