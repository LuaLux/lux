using System.Text;

namespace Nebra.Diagnostics;

/// <summary>
/// Renders a <see cref="Diagnostic"/> as a Rust-style, human-readable block: a coloured
/// <c>severity[Ecode]: message</c> header, a <c>--&gt; file:line:col</c> locator, the offending
/// source line with a caret underline, and optional <c>= help:</c> / <c>= note:</c> guidance.
/// Falls back to a compact single line when no source snippet is available.
/// </summary>
public static class DiagnosticRenderer
{
    private static readonly bool UseColor =
        Environment.GetEnvironmentVariable("NO_COLOR") == null && !Console.IsErrorRedirected;

    private static readonly Dictionary<string, string[]> FileLineCache = new();

    private const string Reset = "[0m";
    private const string Bold = "[1m";
    private const string Red = "[31m";
    private const string Yellow = "[33m";
    private const string Blue = "[34m";
    private const string Cyan = "[36m";
    private const string Dim = "[2m";

    public static string Render(Diagnostic d)
    {
        var (severity, color) = d.Level switch
        {
            DiagnosticLevel.Error => ("error", Red),
            DiagnosticLevel.Warning => ("warning", Yellow),
            _ => ("note", Blue)
        };

        var sb = new StringBuilder();

        sb.Append(C(color + Bold, severity));
        sb.Append(C(color + Bold, $"[E{(ushort)(int)d.Code:X4}]"));
        sb.Append(C(Bold, ": "));
        sb.Append(C(Bold, d.Message));
        sb.Append('\n');

        var span = d.Span;
        var line = SourceLine(span);
        var gutterW = span.StartLn.ToString().Length;
        var pad = new string(' ', gutterW);

        var loc = span.File != null
            ? $"{span.File}:{span.StartLn}:{span.StartCol}"
            : $"line {span.StartLn}:{span.StartCol}";
        sb.Append(pad);
        sb.Append(C(Blue + Bold, "--> "));
        sb.Append(loc);
        sb.Append('\n');

        if (line != null)
        {
            var shown = line.Replace('\t', ' ');
            var startCol = Math.Clamp(span.StartCol, 1, shown.Length + 1);
            int caretCount;
            if (span.EndLn > span.StartLn)
                caretCount = Math.Max(1, shown.Length - (startCol - 1));
            else
                caretCount = Math.Max(1, span.EndCol - span.StartCol);
            caretCount = Math.Max(1, Math.Min(caretCount, shown.Length - (startCol - 1) + 1));

            var bar = C(Blue + Bold, "|");
            sb.Append($"{pad} {bar}\n");
            sb.Append($"{C(Blue + Bold, span.StartLn.ToString())} {bar} {shown}\n");
            sb.Append($"{pad} {bar} {new string(' ', startCol - 1)}{C(color + Bold, new string('^', caretCount))}");
            sb.Append('\n');
        }

        if (d.Help != null)
        {
            sb.Append($"{pad} {C(Cyan + Bold, "= help:")} {d.Help}\n");
        }
        foreach (var note in d.Notes)
        {
            sb.Append($"{pad} {C(Dim, "= note:")} {note}\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string C(string codes, string text) => UseColor ? codes + text + Reset : text;

    private static string? SourceLine(TextSpan span)
    {
        if (span.File == null) return null;
        if (!FileLineCache.TryGetValue(span.File, out var lines))
        {
            try
            {
                lines = File.Exists(span.File)
                    ? File.ReadAllText(span.File).Replace("\r\n", "\n").Split('\n')
                    : [];
            }
            catch
            {
                lines = [];
            }
            FileLineCache[span.File] = lines;
        }

        var idx = span.StartLn - 1;
        return idx >= 0 && idx < lines.Length ? lines[idx] : null;
    }
}
