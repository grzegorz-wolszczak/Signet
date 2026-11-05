using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>Steps that exercise parsing URL fragments according to their target MIME type.</summary>
internal static class UrlFragmentSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((world, arguments) => Parse(world, arguments.Get<string>(0), arguments.Get<string>(1), arguments.Get<string>(2)),
            "{string} is a {} {} fragment");
        steps.Add((world, arguments) => world.LastFragment!.Id.Should().Be(arguments.Get<string>(0)),
            "it indicates an element with ID {string}");
        steps.Add((world, _) => world.LastFragment!.Id.Should().BeEmpty(),
            "it does not indicate an element");
        steps.Add((world, _) => world.LastFragment!.IsMediaFragment.Should().BeTrue(),
            "it is a media fragment");
        steps.Add((world, arguments) => world.LastFragment!.Scheme.Should().Be(arguments.Get<string>(0)),
            "it has scheme {string}");
    }

    private static void Parse(ScenarioWorld world, string fragment, string validity, string kind)
    {
        world.LastFragment = EpubUrlFragment.Parse(fragment, ToMimeType(kind));
        world.LastFragment.IsValid.Should().Be(validity == "valid");
    }

    private static string ToMimeType(string kind) => kind switch
    {
        "HTML" => "text/html",
        "SVG" => "image/svg+xml",
        _ => throw new StepBindingException("Unknown URL fragment MIME type: " + kind),
    };
}
