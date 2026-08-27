using Nebra.IR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Nebra.LPS.Handlers;

public sealed class RenameHandler(NebraWorkspace workspace) : RenameHandlerBase
{
    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.FromResult<WorkspaceEdit?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var nameRef = NodeFinder.FindNameRef(result.Hir, line, col);
        if (nameRef == null || nameRef.Sym == SymID.Invalid)
            return Task.FromResult<WorkspaceEdit?>(null);

        if (!result.Syms.GetByID(nameRef.Sym, out var sym))
            return Task.FromResult<WorkspaceEdit?>(null);

        if (sym.DeclaringNode == NodeID.Invalid)
            return Task.FromResult<WorkspaceEdit?>(null);

        var targetSym = nameRef.Sym;

        // Only names declared in this file can be renamed safely. The edits are collected from this
        // file's HIR alone, so renaming a symbol that another file declares would rewrite the
        // import and the uses here while leaving the declaration untouched, which does not compile.
        if (result.ImportedDeclarations.ContainsKey(targetSym))
            return Task.FromResult<WorkspaceEdit?>(null);

        if (result.FileMap.TryGetValue(sym.DeclaringNode, out var declaringFile)
            && !string.IsNullOrEmpty(declaringFile)
            && !SamePath(declaringFile, request.TextDocument.Uri.GetFileSystemPath()))
            return Task.FromResult<WorkspaceEdit?>(null);

        var allRefs = NodeFinder.CollectAllNameRefs(result.Hir);
        var edits = allRefs
            .Where(nr => nr.Sym == targetSym)
            .Select(nr => new TextEdit
            {
                Range = NebraWorkspace.SpanToRange(nr.Span),
                NewText = request.NewName
            })
            .ToList();

        var docUri = DocumentUri.Parse(result.Uri);
        var workspaceEdit = new WorkspaceEdit
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
            {
                [docUri] = edits
            }
        };

        return Task.FromResult<WorkspaceEdit?>(workspaceEdit);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("nebra"),
            PrepareProvider = true
        };
    }
    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

}
