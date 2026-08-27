using Nebra.IR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Nebra.LPS.Handlers;

public sealed class ReferencesHandler(NebraWorkspace workspace) : ReferencesHandlerBase
{
    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.FromResult<LocationContainer?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var nameRef = NodeFinder.FindNameRef(result.Hir, line, col);
        if (nameRef == null || nameRef.Sym == SymID.Invalid)
            return Task.FromResult<LocationContainer?>(null);

        var targetSym = nameRef.Sym;
        var allRefs = NodeFinder.CollectAllNameRefs(result.Hir);
        var locations = allRefs
            .Where(nr => nr.Sym == targetSym)
            .Select(nr => new Location
            {
                Uri = DocumentUri.Parse(result.Uri),
                Range = NebraWorkspace.SpanToRange(nr.Span)
            })
            .ToList();

        if (request.Context.IncludeDeclaration &&
            result.Syms.GetByID(targetSym, out var sym) &&
            sym.DeclaringNode != NodeID.Invalid &&
            result.NodeRegistry.TryGetValue(sym.DeclaringNode, out var declNode))
        {
            locations.Insert(0, new Location
            {
                Uri = DocumentUri.Parse(result.Uri),
                Range = NebraWorkspace.SpanToRange(declNode.Span)
            });
        }

        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("nebra")
        };
    }
}
