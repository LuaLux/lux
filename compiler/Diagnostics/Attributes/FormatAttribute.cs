namespace Nebra.Diagnostics;

/// <summary>
/// Sets a message format for a <see cref="DiagnosticCode"/>. The format is basically a description that explains
/// the diagnostic and can include {0..n}-placeeholders to fill in specific information about the diagnostic, such as
/// the name of a type, the expected type, the actual type, etc.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
internal class FormatAttribute(string format) : Attribute
{
    /// <summary>
    /// The message format of the diagnostic.
    /// </summary>
    internal string Format { get; } = format;
}