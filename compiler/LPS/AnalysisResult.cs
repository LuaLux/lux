using Antlr4.Runtime;
using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.LPS;

public sealed record ImportedDecl(string FilePath, TextSpan Span, Node DeclNode);

public sealed class AnalysisResult
{
    public required string Uri { get; init; }
    public required string FilePath { get; init; }
    public required string SourceText { get; init; }
    public required PreparsedFile File { get; init; }
    public required PackageContext Package { get; init; }
    public required DiagnosticsBag Diagnostics { get; init; }
    public required CommonTokenStream TokenStream { get; init; }
    public required Dictionary<NodeID, Node> NodeRegistry { get; init; }
    public required Dictionary<NodeID, string> FileMap { get; init; }

    public Dictionary<SymID, ImportedDecl> ImportedDeclarations { get; set; } = new();

    public IRScript Hir => File.Hir;
    public SymbolArena Syms => Package.Syms;
    public ScopeGraph Scopes => Package.Scopes;
    public TypeTable Types => Package.Types;
}
