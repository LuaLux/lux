using Nebra.IR;

namespace Nebra.Doc;

/// <summary>
/// Walks an <see cref="IRScript"/> and attaches a <see cref="DocComment"/> to
/// each declaration that has a LuaCATS-style comment immediately above it.
/// Runs once per file right after preparse; subsequent passes (LSP, doc-site,
/// DeclGen) read <c>Decl.Doc</c> directly without touching the source again.
/// </summary>
public static class DocBinder
{
    public static void Bind(IRScript script, string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        foreach (var stmt in script.Body)
            BindStmt(stmt, source);
    }

    private static void BindStmt(Stmt stmt, string source)
    {
        switch (stmt)
        {
            case ExportStmt exp:
                BindDecl(exp.Declaration, source, exp.Span.StartLn);
                break;
            case Decl decl:
                BindDecl(decl, source, decl.Span.StartLn);
                break;
        }
    }

    private static void BindDecl(Decl decl, string source, int decoratedLine)
    {
        decl.Doc = DocCommentParser.ExtractAt(source, decoratedLine);

        switch (decl)
        {
            case ClassDecl cd:
                if (cd.Constructor != null)
                    cd.Constructor.Doc = DocCommentParser.ExtractAt(source, cd.Constructor.Span.StartLn);
                foreach (var f in cd.Fields)
                    f.Doc = DocCommentParser.ExtractAt(source, f.Span.StartLn);
                foreach (var m in cd.Methods)
                    m.Doc = DocCommentParser.ExtractAt(source, m.Span.StartLn);
                foreach (var a in cd.Accessors)
                    a.Doc = DocCommentParser.ExtractAt(source, a.Span.StartLn);
                break;
            case InterfaceDecl iface:
                foreach (var f in iface.Fields)
                    f.Doc = DocCommentParser.ExtractAt(source, f.Span.StartLn);
                foreach (var m in iface.Methods)
                    m.Doc = DocCommentParser.ExtractAt(source, m.Span.StartLn);
                break;
            case EnumDecl ed:
                foreach (var m in ed.Members)
                    m.Doc = DocCommentParser.ExtractAt(source, m.Span.StartLn);
                break;
            case DeclareModuleDecl dmd:
                foreach (var member in dmd.Members)
                    BindDecl(member, source, member.Span.StartLn);
                break;
        }
    }
}
