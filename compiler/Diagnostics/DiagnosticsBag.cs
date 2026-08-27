using System.Collections.Concurrent;

namespace Nebra.Diagnostics;

/// <summary>
/// The diagnostics bag is a collection of diagnostics that can be used to report errors, warnings, and informational messages during the compilation process.
/// It allows the compiler to collect diagnostics from various stages of the compilation process and report them to the user in a consistent manner.
/// </summary>
public sealed class DiagnosticsBag
{
    internal static IDictionary<DiagnosticCode, DiagnosticData> DiagnosticToData { get; } =
        new ConcurrentDictionary<DiagnosticCode, DiagnosticData>();

    static DiagnosticsBag()
    {
        var codeType = typeof(DiagnosticCode);
        var fields = codeType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var field in fields)
        {
            var code = (DiagnosticCode)field.GetValue(null)!;
            if (field.GetCustomAttributes(typeof(CategoryAttribute), false).FirstOrDefault() is not CategoryAttribute
                    categoryAttribute ||
                field.GetCustomAttributes(typeof(LevelAttribute), false).FirstOrDefault() is not LevelAttribute
                    levelAttribute ||
                field.GetCustomAttributes(typeof(FormatAttribute), false).FirstOrDefault() is not FormatAttribute
                    messageAttribute)
            {
                throw new InvalidOperationException($"Diagnostic code {code} is missing required attributes.");
            }

            var helpAttribute = field.GetCustomAttributes(typeof(HelpAttribute), false).FirstOrDefault() as HelpAttribute;
            DiagnosticToData[code] = new DiagnosticData(code, categoryAttribute.Category, levelAttribute.Level, messageAttribute.Format, helpAttribute?.Help);
        }
    }
    
    private readonly ConcurrentBag<Diagnostic> _diagnostics = [];

    /// <summary>
    /// Gets the diagnostics in the bag.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();
    
    /// <summary>
    /// Gets a value indicating whether the bag contains any diagnostics with a level of <see cref="DiagnosticLevel.Error"/>.
    /// This is used to determine whether the compilation process should be aborted due to errors.
    /// </summary>
    public bool HasErrors => ErrorCount > 0;
    
    /// <summary>
    /// Gets the number of diagnostics in the bag with a level of <see cref="DiagnosticLevel.Error"/>.
    /// </summary>
    public int ErrorCount => Diagnostics.Count(d => d.Level == DiagnosticLevel.Error);
    
    /// <summary>
    /// Gets a value indicating whether the bag contains any diagnostics with a level of <see cref="DiagnosticLevel.Warning"/>.
    /// </summary>
    public bool HasWarnings => WarningCount > 0;
    
    /// <summary>
    /// Gets the number of diagnostics in the bag with a level of <see cref="DiagnosticLevel.Warning"/>.
    /// </summary>
    public int WarningCount => Diagnostics.Count(d => d.Level == DiagnosticLevel.Warning);
    
    /// <summary>
    /// Gets a value indicating whether the bag contains any diagnostics with a level of <see cref="DiagnosticLevel.Info"/>.
    /// </summary>
    public int InfoCount => Diagnostics.Count(d => d.Level == DiagnosticLevel.Info);

    /// <summary>
    /// Reports a diagnostic to the bag. This is used to add a diagnostic to the bag, which can then be retrieved later using the <see cref="Diagnostics"/> property.
    /// </summary>
    public void Report(TextSpan span, DiagnosticCode code, params object[] args)
        => ReportCore(span, code, null, null, args);

    /// <summary>
    /// Reports a diagnostic with an instance-specific <paramref name="help"/> line (and optional
    /// <paramref name="notes"/>) that override the code's static <c>[Help]</c>. Used where the
    /// guidance depends on the situation, e.g. translated parser errors.
    /// </summary>
    public void ReportWithHelp(TextSpan span, DiagnosticCode code, string? help, IReadOnlyList<string>? notes, params object[] args)
        => ReportCore(span, code, help, notes, args);

    private void ReportCore(TextSpan span, DiagnosticCode code, string? helpOverride, IReadOnlyList<string>? notes, object[] args)
    {
        if (!DiagnosticToData.TryGetValue(code, out var data))
        {
            throw new InvalidOperationException($"Diagnostic code {code} is not registered in the diagnostics bag.");
        }

        var message = string.Format(data.Format, args);
        var help = helpOverride ?? (data.Help != null ? string.Format(data.Help, args) : null);
        var diagnostic = new Diagnostic(data.Level, data.Category, code, message, span, help, notes);
        _diagnostics.Add(diagnostic);
    }
}