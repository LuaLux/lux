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
}
