using System;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>
/// A step needs something the port does not have yet (the checker, a report writer, …). It is not a failure of the
/// port: a scenario of a feature that is not enabled is skipped with the reason this carries.
/// </summary>
internal sealed class ScenarioNotImplementedException : Exception
{
    public ScenarioNotImplementedException(string reason)
        : base(reason)
    {
    }
}

/// <summary>A step of a scenario has no binding, or more than one.</summary>
internal sealed class StepBindingException : Exception
{
    public StepBindingException(string message)
        : base(message)
    {
    }
}
