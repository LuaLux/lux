using Nebra.Configuration;
using Nebra.Diagnostics;

namespace Nebra.IR;

internal partial class IRVisitor(string? filename, IDAlloc<NodeID> nodeAlloc, DiagnosticsBag diag, Config? config = null) : NebraBaseVisitor<Node>
{
    private readonly Config _config = config ?? new Config();

    public override Node VisitScript(NebraParser.ScriptContext context)
    {
        var (body, ret) = VisitBlockContent(context.block());
        var prefix = BuildAutoImports(context);
        if (prefix.Count > 0)
        {
            prefix.AddRange(body);
            body = prefix;
        }
        return new IRScript(NewNodeID, SpanFromCtx(context))
        {
            Body = body,
            Return = ret
        };
    }

    private List<Stmt> BuildAutoImports(NebraParser.ScriptContext context)
    {
        var result = new List<Stmt>();
        if (_config.Code.AutoImports.Count == 0) return result;

        foreach (var entry in _config.Code.AutoImports)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            if (IsAutoImportSelfReference(entry)) continue;
            var nsName = DeriveAutoImportNamespace(entry);
            if (nsName == null) continue;

            var span = SpanFromCtx(context);
            var module = new NameRef(entry, span);
            result.Add(new ImportStmt(NewNodeID, span, ImportKind.Namespace, module)
            {
                Alias = new NameRef(nsName, span),
                IsTypeOnly = !_config.Code.AutoImportsEmit,
            });
        }
        return result;
    }

    /// <summary>
    /// Detects whether the current file IS the auto-imported target so we
    /// don't make Shared/Index.neb import itself. Match is done on filename
    /// stem because the auto-import entry is path-like ("src/Shared/Index")
    /// while filename is filesystem-absolute.
    /// </summary>
    private bool IsAutoImportSelfReference(string entry)
    {
        if (string.IsNullOrEmpty(filename)) return false;
        // Compare normalised tail of the filename against the entry, ignoring
        // a `.neb` suffix and case differences (Windows / macOS paths).
        var fileStem = filename.Replace('\\', '/');
        if (fileStem.EndsWith(".neb", StringComparison.OrdinalIgnoreCase))
            fileStem = fileStem[..^4];
        var entryNorm = entry.Replace('\\', '/').TrimStart('.', '/');
        return fileStem.EndsWith("/" + entryNorm, StringComparison.OrdinalIgnoreCase)
            || fileStem.Equals(entryNorm, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks the namespace identifier for an auto-import path. Defaults to
    /// the last path segment; if that segment is <c>Index</c> (or its
    /// lowercase / capitalised variants) the parent segment is used instead,
    /// matching how <c>Shared/Index</c> is mentally "the Shared module".
    /// </summary>
    private static string? DeriveAutoImportNamespace(string entry)
    {
        var parts = entry.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var last = parts[^1];
        if (last.EndsWith(".neb", StringComparison.OrdinalIgnoreCase))
            last = last[..^4];
        if (string.Equals(last, "Index", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
        {
            var prev = parts[^2];
            if (prev.EndsWith(".neb", StringComparison.OrdinalIgnoreCase))
                prev = prev[..^4];
            return SanitizeIdent(prev);
        }
        return SanitizeIdent(last);
    }

    private static string? SanitizeIdent(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var s = new string(chars);
        if (s.Length == 0 || char.IsDigit(s[0])) s = "_" + s;
        return s;
    }
}
