using System;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>Runs the steps of a scenario in order.</summary>
internal static class ScenarioRunner
{
    /// <summary>
    /// Runs the scenario. A failed assertion comes out as the exception the assertion threw, wrapped with the step it
    /// belongs to; <see cref="ScenarioNotImplementedException"/> comes out as it is.
    /// </summary>
    public static void Run(ScenarioDefinition scenario, StepRegistry registry, ScenarioWorld world)
    {
        try
        {
            foreach (StepDefinition step in scenario.Steps)
            {
                (Action<ScenarioWorld, StepArguments> action, object?[] parameters) = registry.Resolve(step);
                try
                {
                    action(world, new StepArguments(parameters, step));
                }
                catch (ScenarioNotImplementedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"{scenario.FeaturePath}:{step.Line}: {step.Keyword}{step.Text}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
        finally
        {
            // Upstream: the files named by "@output:<path>" tags are deleted after the scenario.
            foreach (string tag in scenario.Tags.Where(t => t.StartsWith("@output:", StringComparison.Ordinal)))
            {
                string file = world.ResolvePath(tag["@output:".Length..]);
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            foreach (string file in world.Deletes.Where(File.Exists))
            {
                File.Delete(file);
            }
        }
    }
}
