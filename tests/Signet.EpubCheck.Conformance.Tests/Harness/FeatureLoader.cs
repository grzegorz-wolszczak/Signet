using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>Reads .feature files into <see cref="ScenarioDefinition"/>s: backgrounds merged, scenario outlines expanded.</summary>
internal static class FeatureLoader
{
    /// <summary>Every scenario of every .feature file under <paramref name="root"/>, in the order of the paths and of the files.</summary>
    public static IReadOnlyList<ScenarioDefinition> LoadAll(string root)
    {
        List<ScenarioDefinition> all = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.feature", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            all.AddRange(Load(File.ReadAllText(file), relative));
        }

        return all;
    }

    /// <summary>The scenarios of one .feature text.</summary>
    /// <param name="text">The text of the .feature file.</param>
    /// <param name="featurePath">The path the scenarios report as their file.</param>
    public static IReadOnlyList<ScenarioDefinition> Load(string text, string featurePath)
    {
        Feature? feature = new Parser().Parse(new StringReader(text)).Feature;
        if (feature is null)
        {
            return [];
        }

        List<string> featureTags = feature.Tags.Select(t => t.Name).ToList();
        List<StepDefinition> featureBackground = feature.Children.OfType<Background>().SelectMany(b => b.Steps.Select(ToStep)).ToList();
        List<ScenarioDefinition> scenarios = [];

        foreach (IHasLocation child in feature.Children)
        {
            switch (child)
            {
                case Scenario scenario:
                    scenarios.AddRange(Expand(scenario, featurePath, feature.Name, null, featureTags, featureBackground));
                    break;
                case Rule rule:
                    List<string> ruleTags = [.. featureTags, .. rule.Tags.Select(t => t.Name)];
                    List<StepDefinition> background = [.. featureBackground, .. rule.Children.OfType<Background>().SelectMany(b => b.Steps.Select(ToStep))];
                    foreach (Scenario inRule in rule.Children.OfType<Scenario>())
                    {
                        scenarios.AddRange(Expand(inRule, featurePath, feature.Name, rule.Name, ruleTags, background));
                    }

                    break;
            }
        }

        return scenarios;
    }

    private static IEnumerable<ScenarioDefinition> Expand(
        Scenario scenario, string featurePath, string feature, string? rule, List<string> inheritedTags, List<StepDefinition> background)
    {
        List<string> tags = [.. inheritedTags, .. scenario.Tags.Select(t => t.Name)];
        List<StepDefinition> steps = scenario.Steps.Select(ToStep).ToList();
        if (!scenario.Examples.Any())
        {
            yield return new ScenarioDefinition(featurePath, feature, rule, scenario.Name, tags, [.. background, .. steps], scenario.Location.Line);
            yield break;
        }

        foreach (Examples examples in scenario.Examples)
        {
            string[] header = examples.TableHeader.Cells.Select(c => c.Value).ToArray();
            foreach (TableRow row in examples.TableBody)
            {
                Dictionary<string, string> values = header.Zip(row.Cells.Select(c => c.Value)).ToDictionary(p => p.First, p => p.Second);
                string Fill(string s) => Regex.Replace(s, "<([^<>]+)>", m => values.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);
                IEnumerable<StepDefinition> filled = steps.Select(s => s with
                {
                    Text = Fill(s.Text),
                    DocString = s.DocString is null ? null : Fill(s.DocString),
                    Table = s.Table?.Select(r => (IReadOnlyList<string>)r.Select(Fill).ToList()).ToList(),
                });
                yield return new ScenarioDefinition(
                    featurePath, feature, rule, Fill(scenario.Name), [.. tags, .. examples.Tags.Select(t => t.Name)],
                    [.. background, .. filled], row.Location.Line);
            }
        }
    }

    private static StepDefinition ToStep(Step step) => new(
        step.Keyword,
        step.Text,
        step.DocString?.Content,
        step.DataTable?.Rows.Select(r => (IReadOnlyList<string>)r.Cells.Select(c => c.Value).ToList()).ToList(),
        step.Location.Line);
}
