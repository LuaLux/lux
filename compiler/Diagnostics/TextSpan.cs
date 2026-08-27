using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Nebra.Diagnostics;

/// <summary>
/// A text span is an object that represents a span of text in a source file. It is used to indicate the location of a diagnostic in the source code.
/// </summary>
public sealed class TextSpan(string? file, int startLn, int startCol, int endLn, int endCol)
{
    /// <summary>
    /// A special value that represents an empty text span. This is used to indicate that a diagnostic does not have a specific location in the source code.
    /// </summary>
    public static readonly TextSpan Empty = new(null, 1, 1, 1, 1);
    
    /// <summary>
    /// The name (path) of the file that the diagnostic is located in. This may be null if the text span is
    /// not located in a file, such as in the REPL.
    /// </summary>
    public string? File { get; } = file;
    
    /// <summary>
    /// The starting line of the text span. This is a 1-based index, meaning that the first line of the file is line 1.
    /// </summary>
    public int StartLn { get; } = startLn;

    /// <summary>
    /// The starting column of the text span. This is a 1-based index, meaning that the first column of the line is column 1.
    /// </summary>
    public int StartCol { get; } = startCol;
    
    /// <summary>
    /// The ending line of the text span. This is a 1-based index, meaning that the first line of the file is line 1.
    /// </summary>
    public int EndLn { get; } = endLn;
    
    /// <summary>
    /// The ending column of the text span. This is a 1-based index, meaning that the first column of the line is column 1.
    /// </summary>
    public int EndCol { get; } = endCol;
    
    /// <summary>
    /// Creates a new text span with the given start and end line and column. The file is set to null.
    /// </summary>
    public TextSpan(int startLn, int startCol, int endLn, int endCol) : this(null, startLn, startCol, endLn, endCol)
    {
    }

    /// <summary>
    /// Returns a string representation of the text span. If the file is not null, the string will be in the format "file(startLn,startCol)-(endLn,endCol)".
    /// If the file is null, the string will be in the format "(startLn,startCol)-(endLn,endCol)".
    /// </summary>
    public override string ToString()
    {
        if (File == null)
        {
            return $"({StartLn},{StartCol})-({EndLn},{EndCol})";
        }
        
        return $"{File}({StartLn},{StartCol})-({EndLn},{EndCol})";
    }

    /// <summary>
    /// Creates a new text span from the given ANTLR token.
    /// </summary>
    /// <param name="token">The ANTLR token to create the text span from.</param>
    /// <param name="file">The name (path) of the file that the diagnostic is located in. This may be null if the text span is not located in a file, such as in the REPL.</param>
    /// <returns>The text span created from the given ANTLR token.</returns>
    public static TextSpan Of(IToken token, string? file = null)
    {
        return new TextSpan(file, token.Line, token.Column + 1, token.Line, token.Column + token.Text?.Length ?? 0 + 1);
    }
    
    /// <summary>
    /// Creates a new text span from the given ANTLR terminal node.
    /// </summary>
    /// <param name="node">The ANTLR terminal node to create the text span from.</param>
    /// <param name="file">The name (path) of the file that the diagnostic is located in. This may be null if the text span is not located in a file, such as in the REPL.</param>
    /// <returns>The text span created from the given ANTLR terminal node.</returns>
    public static TextSpan Of(ITerminalNode node, string? file = null)
    {
        return Of(node.Symbol, file);
    }

    public static TextSpan Combine(TextSpan a, TextSpan b)
    {
        int startLn, startCol, endLn, endCol;
        if (a.StartLn < b.StartLn || (a.StartLn == b.StartLn && a.StartCol <= b.StartCol))
        {
            startLn = a.StartLn;
            startCol = a.StartCol;
        }
        else
        {
            startLn = b.StartLn;
            startCol = b.StartCol;
        }

        if (a.EndLn > b.EndLn || (a.EndLn == b.EndLn && a.EndCol >= b.EndCol))
        {
            endLn = a.EndLn;
            endCol = a.EndCol;
        }
        else
        {
            endLn = b.EndLn;
            endCol = b.EndCol;
        }

        return new TextSpan(a.File ?? b.File, startLn, startCol, endLn, endCol);
    }
}