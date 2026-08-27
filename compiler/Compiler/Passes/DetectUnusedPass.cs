using Nebra.IR;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The detect unused pass is responsible for detecting unused variables, functions, and other declarations in the
/// source code. It helps to identify and remove any code that is not being used, which can improve the performance and
/// readability of the code.
/// </summary>
public sealed class DetectUnusedPass() : Pass(PassName, PassScope.PerBuild)
{
    public const string PassName = "DetectUnused";

    public override bool Run(PassContext context)
    {
        foreach (var pkg in context.Pkgs)
        {
            foreach (var f in pkg.Files)
            {
                MarkStmtListUnused(context, pkg, f.Hir.Body);

                if (f.Hir.Return != null)
                {
                    MarkStmtUnused(context, pkg, f.Hir.Return);
                }
            }
        }
        
        foreach (var pkg in context.Pkgs)
        {
            foreach (var f in pkg.Files)
            {
                TrackStmtListUsage(context, pkg, f.Hir.Body);

                if (f.Hir.Return != null)
                {
                    TrackStmtUsage(context, pkg, f.Hir.Return);
                }
            }
        }
        
        return true;
    }
    
    private void MarkStmtListUnused(PassContext pc, PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            MarkStmtUnused(pc, pkg, stmt);
        }
    }

    private void MarkStmtUnused(PassContext pc, PackageContext pkg, Stmt stmt)
    {
        switch (stmt)
        {
            case Decl decl:
                MarkDeclUnused(pc, pkg, decl);
                break;
            case DoBlockStmt doBlockStmt:
                MarkStmtListUnused(pc, pkg, doBlockStmt.Body);
                break;
            case WhileStmt whileStmt:
                MarkStmtListUnused(pc, pkg, whileStmt.Body);
                break;
            case RepeatStmt repeatStmt:
                MarkStmtListUnused(pc, pkg, repeatStmt.Body);
                break;
            case IfStmt ifStmt:
                MarkStmtListUnused(pc, pkg, ifStmt.Body);
                
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    MarkStmtListUnused(pc, pkg, elseIf.Body);
                }
                
                if (ifStmt.ElseBody != null)
                {
                    MarkStmtListUnused(pc, pkg, ifStmt.ElseBody);
                }
                break;
            case NumericForStmt numericForStmt:
            {
                if (pkg.Syms.GetByID(numericForStmt.VarName.Sym, out var sym))
                {
                    sym.Flags |= SymbolFlags.Unused;
                }
                
                MarkStmtListUnused(pc, pkg, numericForStmt.Body);
                break;
            }
            case GenericForStmt genericForStmt:
            {
                foreach (var varName in genericForStmt.VarNames)
                {
                    if (pkg.Syms.GetByID(varName.Sym, out var sym))
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }
                
                MarkStmtListUnused(pc, pkg, genericForStmt.Body);
                break;
            }
            case ImportStmt importStmt:
            {
                if (importStmt.Alias != null)
                {
                    if (pkg.Syms.GetByID(importStmt.Alias.Sym, out var sym))
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }
                
                break;
            }
            case ExportStmt exportStmt:
                MarkDeclUnused(pc, pkg, exportStmt.Declaration);
                break;
            case MatchStmt matchStmt:
                foreach (var arm in matchStmt.Arms)
                {
                    if (arm.Pattern.Kind == MatchPatternKind.TypeBinding && arm.Pattern.Binding != null)
                    {
                        if (pkg.Syms.GetByID(arm.Pattern.Binding.Sym, out var sym))
                            sym.Flags |= SymbolFlags.Unused;
                    }
                    MarkStmtListUnused(pc, pkg, arm.Body);
                }
                break;
            case BreakStmt:
            case ContinueStmt:
            case GuardStmt:
                break;
            case DeferStmt ds:
                if (ds.Block != null) MarkStmtListUnused(pc, pkg, ds.Block);
                break;
        }
    }

    private void MarkDeclUnused(PassContext pc, PackageContext pkg, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl functionDecl:
            {
                foreach (var param in functionDecl.Parameters)
                {
                    if (pkg.Syms.GetByID(param.Name.Sym, out var sym))
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }
                
                MarkStmtListUnused(pc, pkg, functionDecl.Body);
                
                if (functionDecl.ReturnStmt != null)
                {
                    MarkStmtUnused(pc, pkg, functionDecl.ReturnStmt);
                }
                break;
            }
            case LocalFunctionDecl localFunctionDecl:
            {
                if (pkg.Syms.GetByID(localFunctionDecl.Name.Sym, out var sym))
                {
                    sym.Flags |= SymbolFlags.Unused;
                }
                
                MarkStmtListUnused(pc, pkg, localFunctionDecl.Body);
                
                if (localFunctionDecl.ReturnStmt != null)
                {
                    MarkStmtUnused(pc, pkg, localFunctionDecl.ReturnStmt);
                }
                break;
            }
            case LocalDecl localDecl:
            {
                foreach (var variable in localDecl.Variables)
                {
                    if (pkg.Syms.GetByID(variable.Name.Sym, out var sym))
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }

                break;
            }
            case ClassDecl classDecl:
                if (classDecl.Constructor != null)
                {
                    foreach (var p in classDecl.Constructor.Parameters)
                        if (pkg.Syms.GetByID(p.Name.Sym, out var ps)) ps.Flags |= SymbolFlags.Unused;
                    MarkStmtListUnused(pc, pkg, classDecl.Constructor.Body);
                }
                foreach (var method in classDecl.Methods)
                {
                    foreach (var p in method.Parameters)
                        if (pkg.Syms.GetByID(p.Name.Sym, out var ps)) ps.Flags |= SymbolFlags.Unused;
                    MarkStmtListUnused(pc, pkg, method.Body);
                }
                foreach (var accessor in classDecl.Accessors)
                {
                    foreach (var p in accessor.Parameters)
                        if (pkg.Syms.GetByID(p.Name.Sym, out var ps)) ps.Flags |= SymbolFlags.Unused;
                    MarkStmtListUnused(pc, pkg, accessor.Body);
                }
                break;
            case InterfaceDecl:
                break;
        }
    }

    private void TrackStmtListUsage(PassContext pc, PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            TrackStmtUsage(pc, pkg, stmt);
        }
    }

    private void TrackStmtUsage(PassContext pc, PackageContext pkg, Stmt stmt)
    {
        switch (stmt)
        {
            case Decl decl:
                TrackDeclUsage(pc, pkg, decl);
                break;
            case ExprStmt exprStmt:
                TrackExprUsage(pc, pkg, exprStmt.Expression);
                break;
            case DoBlockStmt doBlockStmt:
                TrackStmtListUsage(pc, pkg, doBlockStmt.Body);
                break;
            case WhileStmt whileStmt:
                TrackExprUsage(pc, pkg, whileStmt.Condition);
                TrackStmtListUsage(pc, pkg, whileStmt.Body);
                break;
            case RepeatStmt repeatStmt:
                TrackStmtListUsage(pc, pkg, repeatStmt.Body);
                TrackExprUsage(pc, pkg, repeatStmt.Condition);
                break;
            case IfStmt ifStmt:
                TrackExprUsage(pc, pkg, ifStmt.Condition);
                TrackStmtListUsage(pc, pkg, ifStmt.Body);
                
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    TrackExprUsage(pc, pkg, elseIf.Condition);
                    TrackStmtListUsage(pc, pkg, elseIf.Body);
                }
                
                if (ifStmt.ElseBody != null)
                {
                    TrackStmtListUsage(pc, pkg, ifStmt.ElseBody);
                }
                break;
            case NumericForStmt numericForStmt:
                TrackExprUsage(pc, pkg, numericForStmt.Start);
                TrackExprUsage(pc, pkg, numericForStmt.Limit);
                if (numericForStmt.Step != null)                {
                    TrackExprUsage(pc, pkg, numericForStmt.Step);
                }
                TrackStmtListUsage(pc, pkg, numericForStmt.Body);
                break;
            case GenericForStmt genericForStmt:
                foreach (var varName in genericForStmt.VarNames)
                {
                    if (pkg.Syms.GetByID(varName.Sym, out var sym))
                    {
                        sym.Flags &= ~SymbolFlags.Unused;
                    }
                }

                foreach (var iterator in genericForStmt.Iterators)
                {
                    TrackExprUsage(pc, pkg, iterator);
                }

                TrackStmtListUsage(pc, pkg, genericForStmt.Body);
                break;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    TrackExprUsage(pc, pkg, value);
                }
                break;
            case ImportStmt importStmt:
                if (importStmt.Alias != null)
                {
                    if (pkg.Syms.GetByID(importStmt.Alias.Sym, out var sym))
                    {
                        sym.Flags &= ~SymbolFlags.Unused;
                    }
                }
                break;
            case ExportStmt exportStmt:
                TrackDeclUsage(pc, pkg, exportStmt.Declaration);
                break;
            case MatchStmt matchStmt:
                TrackExprUsage(pc, pkg, matchStmt.Scrutinee);
                foreach (var arm in matchStmt.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) TrackExprUsage(pc, pkg, arm.Pattern.ValueExpr);
                    if (arm.Guard != null) TrackExprUsage(pc, pkg, arm.Guard);
                    TrackStmtListUsage(pc, pkg, arm.Body);
                }
                break;
            case AssignStmt assignStmt:
                foreach (var target in assignStmt.Targets) TrackExprUsage(pc, pkg, target);
                foreach (var value in assignStmt.Values) TrackExprUsage(pc, pkg, value);
                break;
            case BreakStmt:
            case ContinueStmt:
                break;
            case DeferStmt ds:
                if (ds.Call != null) TrackExprUsage(pc, pkg, ds.Call);
                if (ds.Block != null) TrackStmtListUsage(pc, pkg, ds.Block);
                break;
            case GuardStmt gs:
                TrackExprUsage(pc, pkg, gs.Condition);
                if (gs.ElseExpr != null) TrackExprUsage(pc, pkg, gs.ElseExpr);
                break;
        }
    }

    private void TrackDeclUsage(PassContext pc, PackageContext pkg, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl functionDecl:
            {
                foreach (var param in functionDecl.Parameters)
                {
                    if (pkg.Syms.GetByID(param.Name.Sym, out var sym))
                    {
                        sym.Flags &= ~SymbolFlags.Unused;
                    }
                    else
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }
                
                TrackStmtListUsage(pc, pkg, functionDecl.Body);
                
                if (functionDecl.ReturnStmt != null)
                {
                    TrackStmtUsage(pc, pkg, functionDecl.ReturnStmt);
                }
                
                break;
            }
            case LocalFunctionDecl localFunctionDecl:
            {
                if (pkg.Syms.GetByID(localFunctionDecl.Name.Sym, out var sym))
                {
                    sym.Flags &= ~SymbolFlags.Unused;
                }
                else
                {
                    sym.Flags |= SymbolFlags.Unused;
                }
                
                TrackStmtListUsage(pc, pkg, localFunctionDecl.Body);
                
                if (localFunctionDecl.ReturnStmt != null)
                {
                    TrackStmtUsage(pc, pkg, localFunctionDecl.ReturnStmt);
                }
                
                break;
            }
            case LocalDecl localDecl:
            {
                foreach (var variable in localDecl.Variables)
                {
                    if (pkg.Syms.GetByID(variable.Name.Sym, out var sym))
                    {
                        sym.Flags &= ~SymbolFlags.Unused;
                    }
                    else
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }

                break;
            }
            case ClassDecl classDecl:
                if (classDecl.Constructor != null)
                {
                    TrackStmtListUsage(pc, pkg, classDecl.Constructor.Body);
                    if (classDecl.Constructor.ReturnStmt != null) TrackStmtUsage(pc, pkg, classDecl.Constructor.ReturnStmt);
                }
                foreach (var method in classDecl.Methods)
                {
                    TrackStmtListUsage(pc, pkg, method.Body);
                    if (method.ReturnStmt != null) TrackStmtUsage(pc, pkg, method.ReturnStmt);
                }
                foreach (var accessor in classDecl.Accessors)
                {
                    TrackStmtListUsage(pc, pkg, accessor.Body);
                    if (accessor.ReturnStmt != null) TrackStmtUsage(pc, pkg, accessor.ReturnStmt);
                }
                foreach (var field in classDecl.Fields)
                {
                    if (field.DefaultValue != null) TrackExprUsage(pc, pkg, field.DefaultValue);
                }
                break;
            case InterfaceDecl:
                break;
        }
    }

    private void TrackExprUsage(PassContext pc, PackageContext pkg, Expr expr)
    {
        switch (expr)
        {
            case FunctionDefExpr functionDefExpr:
            {
                foreach (var param in functionDefExpr.Parameters)
                {
                    if (pkg.Syms.GetByID(param.Name.Sym, out var sym))
                    {
                        sym.Flags &= ~SymbolFlags.Unused;
                    }
                    else
                    {
                        sym.Flags |= SymbolFlags.Unused;
                    }
                }

                TrackStmtListUsage(pc, pkg, functionDefExpr.Body);

                if (functionDefExpr.ReturnStmt != null)
                {
                    TrackStmtUsage(pc, pkg, functionDefExpr.ReturnStmt);
                }

                break;
            }
            case BinaryExpr binaryExpr:
                TrackExprUsage(pc, pkg, binaryExpr.Left);
                TrackExprUsage(pc, pkg, binaryExpr.Right);
                break;
            case UnaryExpr unaryExpr:
                TrackExprUsage(pc, pkg, unaryExpr.Operand);
                break;
            case NameExpr nameExpr:
            {
                if (pkg.Syms.GetByID(nameExpr.Name.Sym, out var sym))
                {
                    sym.Flags &= ~SymbolFlags.Unused;
                }

                break;
            }
            case ParenExpr parenExpr:
                TrackExprUsage(pc, pkg, parenExpr.Inner);
                break;
            case DotAccessExpr dotAccessExpr:
                TrackExprUsage(pc, pkg, dotAccessExpr.Object);
                break;
            case IndexAccessExpr indexAccessExpr:
                TrackExprUsage(pc, pkg, indexAccessExpr.Object);
                TrackExprUsage(pc, pkg, indexAccessExpr.Index);
                break;
            case FunctionCallExpr functionCallExpr:
                TrackExprUsage(pc, pkg, functionCallExpr.Callee);
                foreach (var arg in functionCallExpr.Arguments)
                {
                    TrackExprUsage(pc, pkg, arg);
                }
                break;
            case MethodCallExpr methodCallExpr:
                TrackExprUsage(pc, pkg, methodCallExpr.Object);
                foreach (var arg in methodCallExpr.Arguments)
                {
                    TrackExprUsage(pc, pkg, arg);
                }
                break;
            case InterpolatedStringExpr interpolatedStringExpr:
                foreach (var part in interpolatedStringExpr.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        TrackExprUsage(pc, pkg, exprPart.Expression);
                    }
                }
                break;
            case NonNilAssertExpr nonNilAssert:
                TrackExprUsage(pc, pkg, nonNilAssert.Inner);
                break;
            case IncDecExpr incDec:
                TrackExprUsage(pc, pkg, incDec.Target);
                break;
            case TypeCheckExpr typeCheck:
                TrackExprUsage(pc, pkg, typeCheck.Inner);
                break;
            case TypeCastExpr typeCast:
                TrackExprUsage(pc, pkg, typeCast.Inner);
                break;
            case TypeOfExpr typeOf:
                TrackExprUsage(pc, pkg, typeOf.Inner);
                break;
            case InstanceOfExpr instOf:
                if (pkg.Syms.GetByID(instOf.ClassName.Sym, out var instSym))
                    instSym.Flags &= ~SymbolFlags.Unused;
                TrackExprUsage(pc, pkg, instOf.Inner);
                break;
            case TableConstructorExpr tableConstructorExpr:
                foreach (var field in tableConstructorExpr.Fields)
                {
                    if (field.Key != null)
                    {
                        TrackExprUsage(pc, pkg, field.Key);
                    }

                    TrackExprUsage(pc, pkg, field.Value);
                }
                break;
            case AwaitExpr awaitExpr:
                TrackExprUsage(pc, pkg, awaitExpr.Expression);
                break;
            case NewExpr newExpr:
                if (pkg.Syms.GetByID(newExpr.ClassName.Sym, out var newSym))
                    newSym.Flags &= ~SymbolFlags.Unused;
                foreach (var arg in newExpr.Arguments) TrackExprUsage(pc, pkg, arg);
                break;
            case SuperCallExpr superCallExpr:
                foreach (var arg in superCallExpr.Arguments) TrackExprUsage(pc, pkg, arg);
                break;
            case MatchExpr matchExpr:
                TrackExprUsage(pc, pkg, matchExpr.Scrutinee);
                foreach (var arm in matchExpr.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) TrackExprUsage(pc, pkg, arm.Pattern.ValueExpr);
                    if (arm.Guard != null) TrackExprUsage(pc, pkg, arm.Guard);
                    TrackExprUsage(pc, pkg, arm.Value);
                }
                break;
        }
    }
}