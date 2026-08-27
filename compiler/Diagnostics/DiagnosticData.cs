namespace Nebra.Diagnostics;

/// <summary>
/// The internal data of a diagnostic which is stored as attributes on the <see cref="DiagnosticCode"/>.
/// </summary>
internal record DiagnosticData(
    DiagnosticCode Code,
    DiagnosticCategory Category,
    DiagnosticLevel Level,
    string Format,
    string? Help = null
);