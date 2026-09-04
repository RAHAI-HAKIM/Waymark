namespace Waymark.Domain;

/// <summary>
/// Anchor for reflection. The architecture tests need a type in every
/// assembly to load it; without this they silently pass over an empty
/// project, which is the one failure mode an architecture test must not have.
/// </summary>
public sealed class AssemblyMarker;


