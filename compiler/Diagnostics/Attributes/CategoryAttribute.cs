namespace Nebra.Diagnostics;

/// <summary>
/// Sets a <see cref="DiagnosticCategory"/> for a <see cref="DiagnosticCode"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
internal class CategoryAttribute(DiagnosticCategory category) : Attribute
{
    /// <summary>
    /// The category of the diagnostic.
    /// </summary>
    internal DiagnosticCategory Category { get; } = category;
}