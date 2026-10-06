using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Data;
using Signet.Controls.TreeDataGrid.Experimental.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Signet.Controls.TreeDataGrid.Tests.Bindings;

public class TypedBindingTests
{
    [AvaloniaFact]
    public void OneWay_Binding_Single_Link_Should_Get_Value()
    {
        // Arrange
        var source = new TestViewModel { Name = "Test" };
        var target = new TestTarget();

        // Create a binding with a single link (source.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Name);
        var expression = binding.Instance(source);

        // Act
        target.Bind(TestTarget.TextProperty, expression);

        // Assert
        target.Text.Should().Be("Test");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Single_Link_Should_Listen_To_Changes()
    {
        // Arrange
        var source = new TestViewModel { Name = "Test" };
        var target = new TestTarget();

        // Create a binding with a single link (source.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Name);
        var expression = binding.Instance(source);

        // Act
        target.Bind(TestTarget.TextProperty, expression);
        source.Name = "Updated";

        // Assert
        target.Text.Should().Be("Updated");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Single_Link_Should_Listen_And_Write()
    {
        // Arrange
        var source = new TestViewModel { Name = "Test" };
        var target = new TestTarget();

        // Create a binding with a single link (source.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - set up binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - change source
        source.Name = "Updated";

        // Assert
        target.Text.Should().Be("Updated");

        // Act - change target
        target.Text = "UpdatedFromTarget";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        source.Name.Should().Be("UpdatedFromTarget");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Two_Links_Should_Get_Value()
    {
        // Arrange
        var child = new TestViewModel { Name = "Child" };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with two links (source.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Name);
        var expression = binding.Instance(source);

        // Act
        target.Bind(TestTarget.TextProperty, expression);

        // Assert
        target.Text.Should().Be("Child");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Two_Links_Should_Listen_To_Changes()
    {
        // Arrange
        var child = new TestViewModel { Name = "Child" };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with two links (source.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Name);
        var expression = binding.Instance(source);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update leaf property
        child.Name = "UpdatedChild";

        // Assert
        target.Text.Should().Be("UpdatedChild");

        // Act - update intermediate property
        var newChild = new TestViewModel { Name = "NewChild" };
        source.Child = newChild;

        // Assert
        target.Text.Should().Be("NewChild");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Two_Links_Should_Listen_And_Write()
    {
        // Arrange
        var child = new TestViewModel { Name = "Child" };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with two links (source.Child.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update from source
        child.Name = "UpdatedChild";

        // Assert
        target.Text.Should().Be("UpdatedChild");

        // Act - update from target
        target.Text = "UpdatedFromTarget";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        child.Name.Should().Be("UpdatedFromTarget");

        // Act - change intermediate link
        var newChild = new TestViewModel { Name = "NewChild" };
        source.Child = newChild;

        // Assert
        target.Text.Should().Be("NewChild");

        // Act - update from target after changing intermediate
        target.Text = "FinalUpdate";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        newChild.Name.Should().Be("FinalUpdate");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Three_Links_Should_Get_Value()
    {
        // Arrange
        var grandChild = new TestViewModel { Name = "GrandChild" };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with three links (source.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Child!.Name);
        var expression = binding.Instance(source);

        // Act
        target.Bind(TestTarget.TextProperty, expression);

        // Assert
        target.Text.Should().Be("GrandChild");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Three_Links_Should_Listen_To_Changes()
    {
        // Arrange
        var grandChild = new TestViewModel { Name = "GrandChild" };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with three links (source.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Child!.Name);
        var expression = binding.Instance(source);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update leaf property
        grandChild.Name = "UpdatedGrandChild";

        // Assert
        target.Text.Should().Be("UpdatedGrandChild");

        // Act - update middle property
        var newGrandChild = new TestViewModel { Name = "NewGrandChild" };
        child.Child = newGrandChild;

        // Assert
        target.Text.Should().Be("NewGrandChild");

        // Act - update root property
        var newChildWithGrandChild = new TestViewModel
        {
            Name = "NewChild",
            Child = new TestViewModel { Name = "NewestGrandChild" }
        };
        source.Child = newChildWithGrandChild;

        // Assert
        target.Text.Should().Be("NewestGrandChild");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Three_Links_Should_Listen_And_Write()
    {
        // Arrange
        var grandChild = new TestViewModel { Name = "GrandChild" };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with three links (source.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Child!.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - set from target
        target.Text = "UpdatedFromTarget";
        binding.Write!.Invoke(source, target.Text);


        // Assert
        grandChild.Name.Should().Be("UpdatedFromTarget");

        // Act - change intermediate node and update from target
        var newGrandChild = new TestViewModel { Name = "NewGrandChild" };
        child.Child = newGrandChild;

        // Assert initial update
        target.Text.Should().Be("NewGrandChild");

        // Act - update again from target
        target.Text = "AfterIntermediateChange";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        newGrandChild.Name.Should().Be("AfterIntermediateChange");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Four_Links_Should_Get_Value()
    {
        // Arrange
        var greatGrandChild = new TestViewModel { Name = "GreatGrandChild" };
        var grandChild = new TestViewModel { Name = "GrandChild", Child = greatGrandChild };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with four links (source.Child.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Child!.Child!.Name);
        var expression = binding.Instance(source);

        // Act
        target.Bind(TestTarget.TextProperty, expression);

        // Assert
        target.Text.Should().Be("GreatGrandChild");
    }

    [AvaloniaFact]
    public void OneWay_Binding_Four_Links_Should_Listen_To_Changes()
    {
        // Arrange
        var greatGrandChild = new TestViewModel { Name = "GreatGrandChild" };
        var grandChild = new TestViewModel { Name = "GrandChild", Child = greatGrandChild };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with four links (source.Child.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Child!.Child!.Name);
        var expression = binding.Instance(source);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update leaf node
        greatGrandChild.Name = "UpdatedGreatGrandChild";

        // Assert
        target.Text.Should().Be("UpdatedGreatGrandChild");

        // Act - update middle node
        var newGreatGrandChild = new TestViewModel { Name = "NewGreatGrandChild" };
        grandChild.Child = newGreatGrandChild;

        // Assert
        target.Text.Should().Be("NewGreatGrandChild");

        // Act - update higher node
        var newGrandChildWithChild = new TestViewModel
        {
            Name = "NewGrandChild",
            Child = new TestViewModel { Name = "BrandNewGreatGrandChild" }
        };
        child.Child = newGrandChildWithChild;

        // Assert
        target.Text.Should().Be("BrandNewGreatGrandChild");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Four_Links_Should_Listen_And_Write()
    {
        // Arrange
        var greatGrandChild = new TestViewModel { Name = "GreatGrandChild" };
        var grandChild = new TestViewModel { Name = "GrandChild", Child = greatGrandChild };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with four links (source.Child.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Child!.Child!.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - set from target
        target.Text = "UpdatedFromTarget";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        greatGrandChild.Name.Should().Be("UpdatedFromTarget");

        // Act - change leaf node
        var newGreatGrandChild = new TestViewModel { Name = "NewGreatGrandChild" };
        grandChild.Child = newGreatGrandChild;

        // Assert
        target.Text.Should().Be("NewGreatGrandChild");

        // Act - update from target
        target.Text = "AfterLeafChange";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        newGreatGrandChild.Name.Should().Be("AfterLeafChange");

        // Act - update middle node
        var newGrandChildWithChild = new TestViewModel
        {
            Name = "NewGrandChild",
            Child = new TestViewModel { Name = "BrandNewGreatGrandChild" }
        };
        child.Child = newGrandChildWithChild;

        // Assert
        target.Text.Should().Be("BrandNewGreatGrandChild");

        // Act - update from target
        target.Text = "AfterMiddleChange";
        binding.Write!.Invoke(source, target.Text);

        // Assert
        newGrandChildWithChild.Child.Name.Should().Be("AfterMiddleChange");
    }

    [AvaloniaFact]
    public void Binding_Should_Handle_Null_Intermediate_Values()
    {
        // Arrange
        var source = new TestViewModel { Name = "Parent", Child = null };
        var target = new TestTarget();

        // Create a binding that will encounter a null (source.Child.Name)
        var binding = TypedBinding<TestViewModel>.OneWay(vm => vm.Child!.Name);
        var expression = binding.Instance(source);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Assert - binding should have error state due to null reference
        target.Text.Should().BeNull(); // Default value since binding path fails

        // Act - fix the null reference
        var newChild = new TestViewModel { Name = "NewChild" };
        source.Child = newChild;

        // Assert - binding should work now
        target.Text.Should().Be("NewChild");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Single_Link_Should_Update_Via_OnNext()
    {
        // Arrange
        var source = new TestViewModel { Name = "Test" };
        var target = new TestTarget();

        // Create a binding with a single link (source.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - set up binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update via OnNext
        expression.OnNext("UpdatedViaOnNext");

        // Assert
        source.Name.Should().Be("UpdatedViaOnNext");
        target.Text.Should().Be("UpdatedViaOnNext");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Two_Links_Should_Update_Via_OnNext()
    {
        // Arrange
        var child = new TestViewModel { Name = "Child" };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with two links (source.Child.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update via OnNext
        expression.OnNext("UpdatedViaOnNext");

        // Assert
        child.Name.Should().Be("UpdatedViaOnNext");
        target.Text.Should().Be("UpdatedViaOnNext");

        // Act - change intermediate link
        var newChild = new TestViewModel { Name = "NewChild" };
        source.Child = newChild;

        // Assert
        target.Text.Should().Be("NewChild");

        // Act - update via OnNext after changing intermediate
        expression.OnNext("AfterIntermediateChange");

        // Assert
        newChild.Name.Should().Be("AfterIntermediateChange");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Three_Links_Should_Update_Via_OnNext()
    {
        // Arrange
        var grandChild = new TestViewModel { Name = "GrandChild" };
        var child = new TestViewModel { Name = "Child", Child = grandChild };
        var source = new TestViewModel { Name = "Parent", Child = child };
        var target = new TestTarget();

        // Create a binding with three links (source.Child.Child.Name)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Child!.Name);
        var expression = binding.Instance(source, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestTarget.TextProperty, expression);

        // Act - update via OnNext
        expression.OnNext("UpdatedViaOnNext");

        // Assert
        grandChild.Name.Should().Be("UpdatedViaOnNext");

        // Act - change intermediate node
        var newGrandChild = new TestViewModel { Name = "NewGrandChild" };
        child.Child = newGrandChild;

        // Assert initial update
        target.Text.Should().Be("NewGrandChild");

        // Act - update via OnNext after changing intermediate
        expression.OnNext("AfterIntermediateChange");

        // Assert
        newGrandChild.Name.Should().Be("AfterIntermediateChange");
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Should_Propagate_IsExpanded_Toggle()
    {
        // Arrange
        var node = new TestExpandableNode { IsExpanded = false };
        var target = new TestExpandableTarget();

        // Create a binding for IsExpanded property (like in FileTreeNodeModel)
        var binding = TypedBinding<TestExpandableNode>.TwoWay(vm => vm.IsExpanded);
        var expression = binding.Instance(node, BindingMode.TwoWay);

        // Act - initialize binding
        target.Bind(TestExpandableTarget.IsExpandedProperty, expression, binding, node);

        // Assert initial state
        target.IsExpanded.Should().BeFalse();
        node.IsExpanded.Should().BeFalse();
        node.ExpandCallCount.Should().Be(0);
        node.CollapseCallCount.Should().Be(0);

        // Act - simulate ToggleExpandedCommand by directly toggling the property
        node.IsExpanded = true;

        // Assert expanded state propagated to target
        target.IsExpanded.Should().BeTrue();
        node.IsExpanded.Should().BeTrue();
        node.ExpandCallCount.Should().Be(1);
        node.CollapseCallCount.Should().Be(0);

        // Act - toggle back to collapsed
        node.IsExpanded = false;

        // Assert collapsed state propagated to target
        target.IsExpanded.Should().BeFalse();
        node.IsExpanded.Should().BeFalse();
        node.ExpandCallCount.Should().Be(1);
        node.CollapseCallCount.Should().Be(1);

        // Act - toggle via target property, which should invoke Write
        node.IsExpanded = true;

        // Assert expanded state propagated to target
        target.IsExpanded.Should().BeTrue();
        node.IsExpanded.Should().BeTrue();
        node.ExpandCallCount.Should().Be(2);
        node.CollapseCallCount.Should().Be(1);
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Should_Work_With_Multiple_Binding_Expressions()
    {
        // Arrange
        var node = new TestExpandableNode { IsExpanded = false };
        var target1 = new TestExpandableTarget();
        var target2 = new TestExpandableTarget();

        // Create a binding for IsExpanded property
        var binding = TypedBinding<TestExpandableNode>.TwoWay(vm => vm.IsExpanded);

        // Create two separate binding expressions from the same binding
        var expression1 = binding.Instance(node, BindingMode.TwoWay);
        var expression2 = binding.Instance(node, BindingMode.TwoWay);

        // Initialize bindings
        target1.Bind(TestExpandableTarget.IsExpandedProperty, expression1, binding, node);
        target2.Bind(TestExpandableTarget.IsExpandedProperty, expression2, binding, node);

        // Act - change from model
        node.IsExpanded = true;

        // Assert both targets updated
        target1.IsExpanded.Should().BeTrue();
        target2.IsExpanded.Should().BeTrue();

        // Act - change from one target
        target1.IsExpanded = false;

        // Assert all synchronized
        node.IsExpanded.Should().BeFalse();
        target2.IsExpanded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void TwoWay_Binding_Should_Work_With_Multiple_Binding_Expressions_And_Long_Chain()
    {
        // Create a deep chain of 5 TestViewModels
        var level5 = new TestViewModel { Name = "Level5" };
        var level4 = new TestViewModel { Name = "Level4", Child = level5 };
        var level3 = new TestViewModel { Name = "Level3", Child = level4 };
        var level2 = new TestViewModel { Name = "Level2", Child = level3 };
        var level1 = new TestViewModel { Name = "Level1", Child = level2 };
        var root = new TestViewModel { Name = "Root", Child = level1 };

        // Create simple targets with a Text property
        var target1 = new TestTarget();
        var target2 = new TestTarget();

        // Create a binding for the deep Name property (5 levels deep)
        var binding = TypedBinding<TestViewModel>.TwoWay(vm => vm.Child!.Child!.Child!.Child!.Child!.Name);

        // Create two separate binding expressions from the same binding
        var expression1 = binding.Instance(root, BindingMode.TwoWay);
        var expression2 = binding.Instance(root, BindingMode.TwoWay);

        // Initialize bindings
        target1.Bind(TestTarget.TextProperty, expression1);
        target2.Bind(TestTarget.TextProperty, expression2);

        // Assert initial binding
        target1.Text.Should().Be("Level5");
        target2.Text.Should().Be("Level5");

        // Act - change leaf node property
        level5.Name = "Updated Level5";

        // Assert both targets updated
        target1.Text.Should().Be("Updated Level5");
        target2.Text.Should().Be("Updated Level5");

        // Act - replace a middle node in the chain
        var newLevel4 = new TestViewModel
        {
            Name = "New Level4",
            Child = new TestViewModel { Name = "New Level5" }
        };
        level3.Child = newLevel4;

        // Assert both targets updated to the new path
        target1.Text.Should().Be("New Level5");
        target2.Text.Should().Be("New Level5");

        // Act - change leaf node property on the old path
        level5.Name = "Changed from Old Path";

        // Assert both targets still point to the new path
        target1.Text.Should().Be("New Level5");
        target2.Text.Should().Be("New Level5");

        // Act - update from target after chain replacement
        target1.Text = "Changed from Target1";
        binding.Write!.Invoke(root, target1.Text);

        // Assert the new leaf node and other target got updated
        newLevel4.Child!.Name.Should().Be("Changed from Target1");
        target2.Text.Should().Be("Changed from Target1");

        // Act - replace another node higher in the chain
        var newLevel2 = new TestViewModel
        {
            Name = "New Level2",
            Child = new TestViewModel
            {
                Name = "Newest Level3",
                Child = new TestViewModel
                {
                    Name = "Newest Level4",
                    Child = new TestViewModel
                    {
                        Name = "Newest Level5"
                    }
                }
            }
        };
        level1.Child = newLevel2;

        // Assert both targets updated to the new deeply nested chain
        target1.Text.Should().Be("Newest Level5");
        target2.Text.Should().Be("Newest Level5");

        // Act - update from target after major chain replacement
        target2.Text = "Final value";
        binding.Write!.Invoke(root, target2.Text);

        // Assert the new deep leaf node and other target got updated
        newLevel2.Child!.Child!.Child!.Name.Should().Be("Final value");
        target1.Text.Should().Be("Final value");
    }


    // Test model that tracks expand/collapse operations
    private class TestExpandableNode : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public int ExpandCallCount { get; private set; }
        public int CollapseCallCount { get; private set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;

                    // Simulate expand/collapse logic
                    if (value)
                        ExpandCallCount++;
                    else
                        CollapseCallCount++;

                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Simple target for IsExpanded property
    private class TestExpandableTarget : AvaloniaObject
    {
        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<TestExpandableTarget, bool>(nameof(IsExpanded));

        private TypedBinding<TestExpandableNode, bool>? _typedBinding;
        private TestExpandableNode? _sourceNode;

        public void Bind<T>(
            StyledProperty<T> property,
            IObservable<BindingValue<T>> source,
            TypedBinding<TestExpandableNode, bool> typedBinding,
            TestExpandableNode sourceNode
            )
        {
            this.Bind(property, source, BindingPriority.Style);
            _sourceNode = sourceNode;
            _typedBinding = typedBinding;
        }

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set
            {
                SetValue(IsExpandedProperty, value);
                _typedBinding?.Write?.Invoke(_sourceNode!, value);
            }
        }
    }

    // Test view model implementing property change notifications
    private class TestViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private TestViewModel? _child = null;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public TestViewModel? Child
        {
            get => _child;
            set => SetProperty(ref _child, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    // Simple target class with an AvaloniaProperty
    private class TestTarget : AvaloniaObject
    {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<TestTarget, string>(nameof(Text));

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}