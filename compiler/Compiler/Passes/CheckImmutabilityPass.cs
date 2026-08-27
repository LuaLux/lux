using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.Compiler.Passes;

public sealed class CheckImmutabilityPass() : Pass(PassName, PassScope.PerFile)
{
    public const string PassName = "CheckImmutability";

    public override bool Run(PassContext context)
    {
        if (!context.Config.Rules.ImmutableDefault) return true;

        var pkg = context.Pkg!;
        var file = context.File!;
        CheckStmtList(context, pkg, file.Hir.Body);
        if (file.Hir.Return != null)
            CheckStmt(context, pkg, file.Hir.Return);
        return true;
    }

    private static void CheckStmtList(PassContext ctx, PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts) CheckStmt(ctx, pkg, stmt);
    }

    private static void CheckStmt(PassContext ctx, PackageContext pkg, Stmt stmt)
    {
        switch (stmt)
        {
            case AssignStmt assign:
                foreach (var target in assign.Targets)
                    CheckAssignTarget(ctx, pkg, target);
                foreach (var v in assign.Values) CheckExpr(ctx, pkg, v);
                break;
            case LocalDecl ld:
                foreach (var v in ld.Values) CheckExpr(ctx, pkg, v);
                break;
            case ExprStmt es:
                CheckExpr(ctx, pkg, es.Expression);
                break;
            case FunctionDecl fd:
                CheckStmtList(ctx, pkg, fd.Body);
                if (fd.ReturnStmt != null) CheckStmt(ctx, pkg, fd.ReturnStmt);
                break;
            case LocalFunctionDecl lfd:
                CheckStmtList(ctx, pkg, lfd.Body);
                if (lfd.ReturnStmt != null) CheckStmt(ctx, pkg, lfd.ReturnStmt);
                break;
            case DoBlockStmt db:
                CheckStmtList(ctx, pkg, db.Body);
                break;
            case WhileStmt ws:
                CheckExpr(ctx, pkg, ws.Condition);
                CheckStmtList(ctx, pkg, ws.Body);
                break;
            case RepeatStmt rs:
                CheckStmtList(ctx, pkg, rs.Body);
                CheckExpr(ctx, pkg, rs.Condition);
                break;
            case IfStmt ifs:
                CheckExpr(ctx, pkg, ifs.Condition);
                CheckStmtList(ctx, pkg, ifs.Body);
                foreach (var ei in ifs.ElseIfs)
                {
                    CheckExpr(ctx, pkg, ei.Condition);
                    CheckStmtList(ctx, pkg, ei.Body);
                }

                if (ifs.ElseBody != null) CheckStmtList(ctx, pkg, ifs.ElseBody);
                break;
            case NumericForStmt nf:
                CheckExpr(ctx, pkg, nf.Start);
                CheckExpr(ctx, pkg, nf.Limit);
                if (nf.Step != null) CheckExpr(ctx, pkg, nf.Step);
                CheckStmtList(ctx, pkg, nf.Body);
                break;
            case GenericForStmt gf:
                foreach (var iter in gf.Iterators) CheckExpr(ctx, pkg, iter);
                CheckStmtList(ctx, pkg, gf.Body);
                break;
            case ReturnStmt ret:
                foreach (var v in ret.Values) CheckExpr(ctx, pkg, v);
                break;
            case ExportStmt exp:
                CheckStmt(ctx, pkg, exp.Declaration);
                break;
            case MatchStmt ms:
                CheckExpr(ctx, pkg, ms.Scrutinee);
                foreach (var arm in ms.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) CheckExpr(ctx, pkg, arm.Pattern.ValueExpr);
                    if (arm.Guard != null) CheckExpr(ctx, pkg, arm.Guard);
                    CheckStmtList(ctx, pkg, arm.Body);
                }

                break;
            case ClassDecl cd:
                if (cd.Constructor != null)
                {
                    CheckStmtList(ctx, pkg, cd.Constructor.Body);
                    if (cd.Constructor.ReturnStmt != null) CheckStmt(ctx, pkg, cd.Constructor.ReturnStmt);
                }
                foreach (var method in cd.Methods)
                {
                    CheckStmtList(ctx, pkg, method.Body);
                    if (method.ReturnStmt != null) CheckStmt(ctx, pkg, method.ReturnStmt);
                }
                foreach (var accessor in cd.Accessors)
                {
                    CheckStmtList(ctx, pkg, accessor.Body);
                    if (accessor.ReturnStmt != null) CheckStmt(ctx, pkg, accessor.ReturnStmt);
                }
                foreach (var field in cd.Fields)
                {
                    if (field.DefaultValue != null) CheckExpr(ctx, pkg, field.DefaultValue);
                }
                break;
            case InterfaceDecl id:
                foreach (var method in id.Methods)
                {
                    if (method.Body == null) continue;
                    CheckStmtList(ctx, pkg, method.Body);
                    if (method.ReturnStmt != null) CheckStmt(ctx, pkg, method.ReturnStmt);
                }

                break;
            case ExtendDecl ed:
                foreach (var method in ed.Methods)
                {
                    CheckStmtList(ctx, pkg, method.Body);
                    if (method.ReturnStmt != null) CheckStmt(ctx, pkg, method.ReturnStmt);
                }

                break;
            case BreakStmt:
            case ContinueStmt:
            case LabelStmt:
            case GotoStmt:
                break;
            case DeferStmt ds:
                if (ds.Call != null) CheckExpr(ctx, pkg, ds.Call);
                if (ds.Block != null) CheckStmtList(ctx, pkg, ds.Block);
                break;
            case GuardStmt gs:
                CheckExpr(ctx, pkg, gs.Condition);
                if (gs.ElseExpr != null) CheckExpr(ctx, pkg, gs.ElseExpr);
                break;
        }
    }

    private static void CheckExpr(PassContext ctx, PackageContext pkg, Expr expr)
    {
        switch (expr)
        {
            case IncDecExpr incDec:
                CheckAssignTarget(ctx, pkg, incDec.Target);
                break;
            case FunctionCallExpr call:
                CheckExpr(ctx, pkg, call.Callee);
                foreach (var a in call.Arguments) CheckExpr(ctx, pkg, a);
                break;
            case MethodCallExpr mc:
                CheckExpr(ctx, pkg, mc.Object);
                foreach (var a in mc.Arguments) CheckExpr(ctx, pkg, a);
                break;
            case BinaryExpr bin:
                CheckExpr(ctx, pkg, bin.Left);
                CheckExpr(ctx, pkg, bin.Right);
                break;
            case UnaryExpr un:
                CheckExpr(ctx, pkg, un.Operand);
                break;
            case ParenExpr pe:
                CheckExpr(ctx, pkg, pe.Inner);
                break;
            case AwaitExpr aw:
                CheckExpr(ctx, pkg, aw.Expression);
                break;
            case NewExpr ne:
                foreach (var arg in ne.Arguments) CheckExpr(ctx, pkg, arg);
                break;
            case SuperCallExpr sc:
                foreach (var arg in sc.Arguments) CheckExpr(ctx, pkg, arg);
                break;
            case MatchExpr me:
                CheckExpr(ctx, pkg, me.Scrutinee);
                foreach (var arm in me.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) CheckExpr(ctx, pkg, arm.Pattern.ValueExpr);
                    if (arm.Guard != null) CheckExpr(ctx, pkg, arm.Guard);
                    CheckExpr(ctx, pkg, arm.Value);
                }

                break;
        }
    }

    private static void CheckAssignTarget(PassContext ctx, PackageContext pkg, Expr target)
    {
        switch (target)
        {
            case NameExpr ne:
            {
                if (ne.Name.Sym == SymID.Invalid) return;
                if (!pkg.Syms.GetByID(ne.Name.Sym, out var sym)) return;
                if (sym.Flags.HasFlag(SymbolFlags.Immutable))
                {
                    ctx.Diag.Report(target.Span, DiagnosticCode.ErrAssignToImmutable, sym.Name);
                }

                break;
            }
            case DotAccessExpr dot:
            {
                var baseSym = GetBaseSym(pkg, dot.Object);
                if (baseSym != null && baseSym.Flags.HasFlag(SymbolFlags.DeepFreeze))
                {
                    ctx.Diag.Report(target.Span, DiagnosticCode.ErrModifyFrozenTable, baseSym.Name);
                }

                break;
            }
            case IndexAccessExpr idx:
            {
                var baseSym = GetBaseSym(pkg, idx.Object);
                if (baseSym != null && baseSym.Flags.HasFlag(SymbolFlags.DeepFreeze))
                {
                    ctx.Diag.Report(target.Span, DiagnosticCode.ErrModifyFrozenTable, baseSym.Name);
                }

                break;
            }
        }
    }

    private static Symbol? GetBaseSym(PackageContext pkg, Expr expr)
    {
        return expr switch
        {
            NameExpr ne when ne.Name.Sym != SymID.Invalid && pkg.Syms.GetByID(ne.Name.Sym, out var s) => s,
            DotAccessExpr dot => GetBaseSym(pkg, dot.Object),
            IndexAccessExpr idx => GetBaseSym(pkg, idx.Object),
            _ => null
        };
    }
}