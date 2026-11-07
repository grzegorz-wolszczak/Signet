using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>What a step receives: the values of its parameters and the doc string or data table under it.</summary>
internal sealed class StepArguments
{
    public StepArguments(object?[] parameters, StepDefinition step)
    {
        Parameters = parameters;
        Step = step;
    }

    public object?[] Parameters { get; }

    public StepDefinition Step { get; }

    /// <summary>The doc string under the step, or <c>null</c>.</summary>
    public string? DocString => Step.DocString;

    /// <summary>The rows of the data table under the step, or <c>null</c>.</summary>
    public IReadOnlyList<IReadOnlyList<string>>? Table => Step.Table;

    /// <summary>The value of the parameter at <paramref name="index"/>.</summary>
    public T Get<T>(int index) => (T)Parameters[index]!;

    /// <summary>
    /// The last text of a step that takes its text either as a parameter or as a doc string
    /// (<c>stdout is "…"</c> and <c>stdout is</c> with a doc string).
    /// </summary>
    public string Text(int parameterIndex) => DocString ?? Get<string>(parameterIndex);
}

/// <summary>The steps that scenarios can use: expressions bound to what they do.</summary>
internal sealed class StepRegistry
{
    private readonly ParameterTypeRegistry _types = new();
    private readonly List<(StepExpression Expression, Action<ScenarioWorld, StepArguments> Action)> _bindings = [];

    /// <summary>A registry with all the steps of upstream's step definitions.</summary>
    public static StepRegistry CreateDefault()
    {
        StepRegistry registry = new();
        ConfigurationSteps.Register(registry);
        ExecutionSteps.Register(registry);
        FilenameSteps.Register(registry);
        ViewportSteps.Register(registry);
        UrlFragmentSteps.Register(registry);
        AssertionSteps.Register(registry);
        CliSteps.Register(registry);
        ReportSteps.Register(registry);
        return registry;
    }

    /// <summary>Binds one or more expressions (the same step spelled in several ways) to an action.</summary>
    public void Add(Action<ScenarioWorld, StepArguments> action, params string[] expressions)
    {
        foreach (string expression in expressions)
        {
            _bindings.Add((StepExpression.Parse(expression, _types), action));
        }
    }

    /// <summary>The binding of a step, with the values its expression found. Throws when there is none, or more than one.</summary>
    public (Action<ScenarioWorld, StepArguments> Action, object?[] Parameters) Resolve(StepDefinition step)
    {
        List<(Action<ScenarioWorld, StepArguments> Action, object?[] Parameters, string Source)> matches = [];
        foreach ((StepExpression expression, Action<ScenarioWorld, StepArguments> action) in _bindings)
        {
            if (expression.TryMatch(step.Text, out object?[] values))
            {
                matches.Add((action, values, expression.Source));
            }
        }

        // The same action reached through two spellings of one step is not an ambiguity.
        List<(Action<ScenarioWorld, StepArguments> Action, object?[] Parameters, string Source)> distinct =
            matches.GroupBy(m => m.Action).Select(g => g.First()).ToList();
        return distinct.Count switch
        {
            1 => (distinct[0].Action, distinct[0].Parameters),
            0 => throw new StepBindingException($"Undefined step: {step.Keyword}{step.Text} (line {step.Line})."),
            _ => throw new StepBindingException(
                $"Ambiguous step: {step.Keyword}{step.Text} (line {step.Line}) matches {string.Join("; ", distinct.Select(m => m.Source))}."),
        };
    }
}
