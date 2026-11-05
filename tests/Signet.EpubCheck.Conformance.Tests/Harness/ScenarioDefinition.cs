using System.Collections.Generic;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>One step of a scenario, as the .feature file spells it (a scenario outline's placeholders already filled).</summary>
/// <param name="Keyword">Given, When, Then, And or But, with the trailing space.</param>
/// <param name="Text">The text after the keyword.</param>
/// <param name="DocString">The doc string under the step, or <c>null</c>.</param>
/// <param name="Table">The data table under the step (rows of cells), or <c>null</c>.</param>
/// <param name="Line">The line of the step in the .feature file.</param>
internal sealed record StepDefinition(
    string Keyword, string Text, string? DocString, IReadOnlyList<IReadOnlyList<string>>? Table, int Line);

/// <summary>A scenario ready to run: the steps of the background(s) first, then its own (a scenario outline gives one per example).</summary>
/// <param name="FeaturePath">The .feature file, relative to the corpus folder, with <c>/</c> separators.</param>
/// <param name="Feature">The name of the feature.</param>
/// <param name="Rule">The name of the rule the scenario is under, or <c>null</c>.</param>
/// <param name="Name">The name of the scenario.</param>
/// <param name="Tags">The tags of the feature, the rule and the scenario (<c>@spec</c>, <c>@output:…</c>).</param>
/// <param name="Steps">The steps, background first.</param>
/// <param name="Line">The line of the scenario in the .feature file.</param>
internal sealed record ScenarioDefinition(
    string FeaturePath,
    string Feature,
    string? Rule,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<StepDefinition> Steps,
    int Line)
{
    /// <summary>A key that tells the scenario from every other: the file and the line.</summary>
    public string Id => $"{FeaturePath}:{Line}";

    /// <summary>The name a test runner shows.</summary>
    public string DisplayName => Rule is null ? $"{FeaturePath} › {Name} (line {Line})" : $"{FeaturePath} › {Rule} › {Name} (line {Line})";
}
