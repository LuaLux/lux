namespace Nebra.Diagnostics;

/// <summary>
/// The diagnostic itself with all the information about the diagnostic, such as the code, the category, the level, the message format, etc.
/// </summary>
public sealed class Diagnostic(DiagnosticLevel level, DiagnosticCategory category, DiagnosticCode code, string message, TextSpan span, string? help = null, IReadOnlyList<string>? notes = null)
{
    /// <summary>
    /// The level of the diagnostic.
    /// </summary>
    public DiagnosticLevel Level { get; } = level;
    
    /// <summary>
    /// The category of the diagnostic.
    /// </summary>
    public DiagnosticCategory Category { get; } = category;
    
    /// <summary>
    /// The code of the diagnostic.
    /// </summary>
    public DiagnosticCode Code { get; } = code;
    
    /// <summary>
    /// The message of the diagnostic. This is the formatted message that is created by filling in the placeholders
    /// in the message format of the diagnostic code with the specific information about the diagnostic.
    /// </summary>
    public string Message { get; } = message;
    
    /// <summary>
    /// The text span of the diagnostic. This indicates the location of the diagnostic in the source code.
    /// </summary>
    public TextSpan Span { get; } = span;

    /// <summary>Optional "what to do" guidance, rendered as a <c>= help:</c> line.</summary>
    public string? Help { get; } = help;

    /// <summary>Optional extra context lines, rendered as <c>= note:</c> lines.</summary>
    public IReadOnlyList<string> Notes { get; } = notes ?? [];

    public override string ToString()
    {
        return $"{Category}#{Code} @ {Span}: {Message}";
    }
}