using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Nebra.LPS.Handlers;

public sealed class TextDocumentSyncHandler(NebraWorkspace workspace) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "nebra");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        workspace.OnDocumentOpened(request.TextDocument.Uri.ToString(), request.TextDocument.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var text = request.ContentChanges.FirstOrDefault()?.Text;
        if (text != null)
            workspace.OnDocumentChanged(request.TextDocument.Uri.ToString(), text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
        => Unit.Task;

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        workspace.OnDocumentClosed(request.TextDocument.Uri.ToString());
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("nebra"),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = true }
        };
    }
}
