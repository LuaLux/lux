namespace Nebra.Diagnostics;

/// <summary>
/// The diagnostic level is used to indicate the severity of a diagnostic. It is used to determine how a diagnostic
/// should be reported to the user, such as whether it should be treated as an error, warning, or informational message.
/// </summary>
public enum DiagnosticLevel
{
    /// <summary>
    /// An unknown diagnostic level is used for diagnostics that don't fit into any of the other levels.
    /// </summary>
    Unknown,
    /// <summary>
    /// An informational diagnostic is used for diagnostics that are not errors or warnings, but are still useful for the user to know about.
    /// These can contain recommendations for how to write something better.
    /// </summary>
    Info,
    /// <summary>
    /// A warning diagnostic is used for diagnostics that indicate a potential issue in the user's code, but do not prevent the code from being compiled or executed.
    /// </summary>
    Warning,
    /// <summary>
    /// An error diagnostic is used for diagnostics that indicate a definite issue in the user's code that prevents the code from being compiled or executed.
    /// </summary>
    Error,
}