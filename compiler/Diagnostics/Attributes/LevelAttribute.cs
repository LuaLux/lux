namespace Nebra.Diagnostics;

/// <summary>
/// Sets a <see cref="DiagnosticLevel"/> for a <see cref="DiagnosticCode"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
internal class LevelAttribute(DiagnosticLevel level) : Attribute
{
    /// <summary>
    /// The level of the diagnostic.
    /// </summary>
    internal DiagnosticLevel Level { get; } = level;
}