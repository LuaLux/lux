using Nebra.LPS.Handlers;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Nebra.LPS;

public static class NebraLanguageServer
{
    public static async Task RunAsync()
    {
        var workspace = new NebraWorkspace();

        var server = await LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .WithServices(services =>
                {
                    services.AddSingleton(workspace);
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<SemanticTokensHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<RenameHandler>()
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<ExecuteCompileCommandHandler>()
                .OnInitialize((srv, request, ct) =>
                {
                    workspace.Initialize(request.RootUri?.GetFileSystemPath());
                    workspace.SetServer(srv);
                    return Task.CompletedTask;
                });
        }).ConfigureAwait(false);

        await server.WaitForExit;
    }
}
