using System.Text.RegularExpressions;
using Antlr4.Runtime;

namespace Nebra.Diagnostics;

/// <summary>
/// Intercepts ANTLR syntax errors and turns their terse, parser-internal wording
/// ("mismatched input 'x' expecting {...}") into a plain-language message plus a
/// <c>= help:</c> line listing what the parser expected.
/// </summary>
internal sealed partial class DiagnosticsTokenErrorListener(DiagnosticsBag diag, string? filename) : BaseErrorListener
{
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        if (offendingSymbol is CommonToken { Type: NebraParser.Eof })
        {
            diag.Report(TextSpan.Of(offendingSymbol, filename), DiagnosticCode.ErrUnexpectedEOF);
            return;
        }

        var (message, help) = Translate(msg, offendingSymbol);
        diag.ReportWithHelp(TextSpan.Of(offendingSymbol, filename), DiagnosticCode.ErrUnexpectedToken, help, null, message);
    }

    /// <summary>
    /// Maps an ANTLR error message to a friendly (message, help) pair.
    /// </summary>
    private static (string message, string? help) Translate(string msg, IToken offending)
    {
        var found = offending.Text is { Length: > 0 } t && t != "<EOF>" ? $"`{t}`" : "end of input";

        if (msg.StartsWith("missing", StringComparison.Ordinal))
        {
            var m = MissingPattern().Match(msg);
            var what = m.Success ? PrettifyToken(m.Groups[1].Value.Trim()) : "a token";
            return ($"missing {what}", $"insert {what} before {found}");
        }

        if (msg.StartsWith("no viable alternative", StringComparison.Ordinal))
            return ($"unexpected {found}", "the parser could not make sense of the code here");

        if (msg.StartsWith("extraneous input", StringComparison.Ordinal))
        {
            var expecting = Expecting(msg);
            return ($"unexpected {found}", expecting != null ? $"remove it, or expected {expecting} here" : "remove this token");
        }

        if (msg.StartsWith("mismatched input", StringComparison.Ordinal))
        {
            var expecting = Expecting(msg);
            return ($"unexpected {found}", expecting != null ? $"expected {expecting}" : null);
        }

        return ($"unexpected {found}", null);
    }

    /// <summary>
    /// Extracts and prettifies the token set from an ANTLR "…expecting {…}" tail.
    /// </summary>
    private static string? Expecting(string msg)
    {
        var idx = msg.IndexOf("expecting ", StringComparison.Ordinal);
        if (idx < 0) return null;

        var rest = msg[(idx + "expecting ".Length)..].Trim().Trim('{', '}');
        var parts = rest.Split(',').Select(p => PrettifyToken(p.Trim())).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return null;
        if (parts.Count == 1) return parts[0];
        return parts.Count <= 6
            ? string.Join(", ", parts)
            : string.Join(", ", parts.Take(6)) + ", …";
    }

    /// <summary>
    /// Renders a single ANTLR token label: literals as <c>`x`</c>, named tokens in plain words.
    /// </summary>
    private static string PrettifyToken(string tok)
    {
        if (tok.Length >= 2 && tok[0] == '\'' && tok[^1] == '\'')
            return $"`{tok.Trim('\'')}`";

        return tok switch
        {
            "NAME" => "a name",
            "INT" or "HEX" or "FLOAT" or "HEX_FLOAT" => "a number",
            "NORMAL_STRING" or "CHAR_STRING" or "LONG_STRING" => "a string",
            "<EOF>" => "end of input",
            _ => $"`{tok}`"
        };
    }

    [GeneratedRegex(@"missing (.+?) at")]
    private static partial Regex MissingPattern();
}
