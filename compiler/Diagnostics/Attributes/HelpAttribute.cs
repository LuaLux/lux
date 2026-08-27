namespace Nebra.Diagnostics;

/// <summary>
/// Optional "what to do" guidance for a <see cref="DiagnosticCode"/>, rendered as a
/// <c>= help:</c> line beneath the diagnostic. Supports the same <c>{0..n}</c> placeholders
/// as <see cref="FormatAttribute"/>, filled from the same report arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
internal sealed class HelpAttribute(string help) : Attribute
{
    internal string Help { get; } = help;
}
