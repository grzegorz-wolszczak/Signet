namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>
/// Steps that are bound, so that every scenario of upstream is understood, but need a part of the port that does not exist
/// yet. They end the scenario as "not implemented" with the task that brings the part.
/// </summary>
internal static class UnavailableSteps
{
    public static void Register(StepRegistry steps)
    {
        Unavailable(steps, "The JSON and XML report writers are not implemented yet (task 4.2).",
            "the default namespace is {string}",
            "the XML report is well-formed",
            "(the )XPath (value of ){string} is {bool}",
            "(the )XPath (value of ){string} is {int}",
            "(the )XPath (value of ){string} is {string}",
            "(the )XPath {string} exists",
            "the JSON report is valid",
            "JSON at {string} contains {int} items",
            "JSON at {string} is {string}",
            "JSON at {string} is {bool}",
            "JSON at {string} is {int}",
            "JSON at {string} is:",
            "JSON at {string} is empty",
            "JSON at {string} is not null",
            "JSON at {string} has no null values",
            "JSON at {string} are all {string}",
            "JSON at {string} are all {bool}");

        Unavailable(steps, "The WHATWG URL / fragment parser is not implemented yet (task 1.3).",
            "{string} is a {} {} fragment",
            "it indicates an element with ID {string}",
            "it does not indicate an element",
            "it is a media fragment",
            "it has scheme {string}");

        Unavailable(steps, "The viewport parser is not implemented yet (task 3.5).",
            "parsing viewport {string}",
            "no error is returned",
            "the parsed viewport equals {multimap}",
            "error {error} is returned");
    }

    private static void Unavailable(StepRegistry steps, string reason, params string[] expressions) =>
        steps.Add((_, _) => throw new ScenarioNotImplementedException(reason), expressions);
}
