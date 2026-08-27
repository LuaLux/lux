using Antlr4.Runtime;

namespace Nebra.Diagnostics;

/// <summary>
/// Represents a custom error listener for the ANTLR parsing pipeline to intercept and handle syntax errors during the parsing process.
/// </summary>
internal sealed class DiagnosticsSymbolErrorListener(DiagnosticsBag diag, string? filename): IAntlrErrorListener<int>
{
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        var startCol = charPositionInLine + 1;
        diag.Report(new TextSpan(filename, line, startCol, line, startCol), DiagnosticCode.ErrLexerUndefinedToken, msg);
    }
}