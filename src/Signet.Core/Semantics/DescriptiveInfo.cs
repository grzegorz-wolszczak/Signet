namespace Signet.Core.Semantics;

/// <summary>
/// A "display name + description" pair describing a semantic code (guide / landmarks / MARC roles).
/// </summary>
/// <param name="Name">The display name (English).</param>
/// <param name="Description">The description of the code meaning.</param>
public readonly record struct DescriptiveInfo(string Name, string Description);
