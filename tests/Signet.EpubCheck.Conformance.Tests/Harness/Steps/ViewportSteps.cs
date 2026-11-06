using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The steps of the viewport meta syntax scenarios (upstream's <c>ViewportSteps</c>).</summary>
internal static class ViewportSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((world, arguments) =>
        {
            world.ViewportErrors.Clear();
            world.LastViewport = ViewportMeta.Parse(arguments.Get<string>(0), (error, _) => world.ViewportErrors.Add(error));
        }, "parsing viewport {string}");

        steps.Add((world, _) => world.ViewportErrors.Should().BeEmpty("there should be no errors"), "no error is returned");

        steps.Add((world, arguments) =>
        {
            Dictionary<string, List<string>> expected = ParseMultimap(arguments.Get<string>(0));
            ViewportMeta viewport = world.LastViewport!;
            viewport.Names.Should().BeEquivalentTo(expected.Keys);
            foreach ((string name, List<string> values) in expected)
            {
                viewport.GetValues(name).Should().Equal(values);
            }
        }, "the parsed viewport equals {multimap}");

        steps.Add((world, arguments) => world.ViewportErrors.Should().Equal(ToError(arguments.Get<string>(0))),
            "error {error} is returned");
    }

    // Upstream's SharedParameterTypes.multimap: "k1=v1,v2;k2=v3" between quotes.
    private static Dictionary<string, List<string>> ParseMultimap(string mapping) =>
        mapping[1..^1].Split(';').Select(entry => entry.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1].Split(',').ToList(), StringComparer.Ordinal);

    // NULL_OR_EMPTY → NullOrEmpty.
    private static ViewportMetaParseError ToError(string name) =>
        Enum.Parse<ViewportMetaParseError>(string.Concat(name.Split('_').Select(part => part[0] + part[1..].ToLowerInvariant())));
}
