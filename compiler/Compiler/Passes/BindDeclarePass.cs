using Nebra.Compiler.Annotations;
using Nebra.Configuration;
using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The bind declare pass is responsible for binding the declarations in the source code. It takes care of declaring
/// symbols and binding them with their nodes to their respective scopes.
/// </summary>
public sealed class BindDeclarePass() : Pass(PassName, PassScope.PerFile)
{
    public const string PassName = "BindDeclare";

    public override bool Run(PassContext context)
    {
        if (context.Pkg == null || context.File == null)
        {
            return false;
        }

        var rootScope = context.File.BindingScopeOverride ?? context.Pkg.Root;

        if (!BindStmtListScopes(context, context.File.Hir.Body, rootScope))
        {
            return false;
        }

        if (context.File.Hir.Return != null)
        {
            return BindStmtScopes(context, context.File.Hir.Return, rootScope);
        }

        return true;
    }

    private bool BindStmtListScopes(PassContext ctx, List<Stmt> stmts, ScopeID scope)
    {
        foreach (var stmt in stmts)
        {
            if (!BindStmtScopes(ctx, stmt, scope))
            {
                return false;
            }
        }

        return true;
    }

    private bool BindStmtScopes(PassContext ctx, Stmt stmt, ScopeID scope)
    {
        if (stmt == null)
        {
            return true;
        }

        var pkg = ctx.Pkg!;
        pkg.Scopes.BindNode(stmt.ID, scope);

        switch (stmt)
        {
            case Decl decl:
                return BindDeclScopes(ctx, decl, scope);
            case AssignStmt assign:
                foreach (var target in assign.Targets)
                {
                    if (!BindExprScopes(ctx, target, scope))
                    {
                        return false;
                    }
                }

                foreach (var value in assign.Values)
                {
                    if (!BindExprScopes(ctx, value, scope))
                    {
                        return false;
                    }
                }

                return true;
            case ExprStmt exprStmt:
                return BindExprScopes(ctx, exprStmt.Expression, scope);
            case LabelStmt labelStmt:
                // Declare the label as a Variable so subsequent passes can look
                // it up via ResolveNameRef. ResolveNamesPass walks the function
                // body in source order, so labels seen later still need binding
                // in the enclosing scope; we add the symbol here regardless and
                // skip the redeclaration error when a label name reappears.
                if (!pkg.Scopes.Lookup(scope, labelStmt.Name.Name, out _))
                    DeclareSymbol(ctx, scope, labelStmt.Name.Name, SymbolKind.Variable, labelStmt.ID);
                return true;
            case BreakStmt:
            case GotoStmt:
                return true;
            case DoBlockStmt doBlock:
                var doScope = pkg.Scopes.NewScope(scope);
                if (!BindStmtListScopes(ctx, doBlock.Body, doScope))
                {
                    return false;
                }

                return true;
            case WhileStmt whileStmt:
                var whileScope = pkg.Scopes.NewScope(scope);
                if (!BindExprScopes(ctx, whileStmt.Condition, whileScope))
                {
                    return false;
                }

                if (!BindStmtListScopes(ctx, whileStmt.Body, whileScope))
                {
                    return false;
                }

                return true;
            case RepeatStmt repeatStmt:
                var repeatScope = pkg.Scopes.NewScope(scope);
                if (!BindStmtListScopes(ctx, repeatStmt.Body, repeatScope))
                {
                    return false;
                }

                if (!BindExprScopes(ctx, repeatStmt.Condition, repeatScope))
                {
                    return false;
                }

                return true;
            case IfStmt ifStmt:
                var ifScope = pkg.Scopes.NewScope(scope);
                if (!BindExprScopes(ctx, ifStmt.Condition, ifScope))
                {
                    return false;
                }

                if (!BindStmtListScopes(ctx, ifStmt.Body, ifScope))
                {
                    return false;
                }

                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    var elseIfScope = pkg.Scopes.NewScope(scope);
                    if (!BindExprScopes(ctx, elseIf.Condition, elseIfScope))
                    {
                        return false;
                    }

                    if (!BindStmtListScopes(ctx, elseIf.Body, elseIfScope))
                    {
                        return false;
                    }
                }

                if (ifStmt.ElseBody != null)
                {
                    var elseScope = pkg.Scopes.NewScope(scope);
                    if (!BindStmtListScopes(ctx, ifStmt.ElseBody, elseScope))
                    {
                        return false;
                    }
                }

                return true;
            case NumericForStmt numericFor:
                var numericForScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(numericFor.ID, numericForScope);
                DeclareSymbol(ctx, numericForScope, numericFor.VarName.Name, SymbolKind.Variable, numericFor.ID);
                if (!BindExprScopes(ctx, numericFor.Start, numericForScope))
                {
                    return false;
                }

                if (!BindExprScopes(ctx, numericFor.Limit, numericForScope))
                {
                    return false;
                }

                if (numericFor.Step != null)
                {
                    if (!BindExprScopes(ctx, numericFor.Step, numericForScope))
                    {
                        return false;
                    }
                }

                if (!BindStmtListScopes(ctx, numericFor.Body, numericForScope))
                {
                    return false;
                }

                return true;
            case GenericForStmt genericFor:
                var genericForScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(genericFor.ID, genericForScope);
                foreach (var varName in genericFor.VarNames)
                {
                    DeclareSymbol(ctx, genericForScope, varName.Name, SymbolKind.Variable, genericFor.ID);
                }
                foreach (var iterator in genericFor.Iterators)
                {
                    if (!BindExprScopes(ctx, iterator, genericForScope))
                    {
                        return false;
                    }
                }

                if (!BindStmtListScopes(ctx, genericFor.Body, genericForScope))
                {
                    return false;
                }

                return true;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    if (!BindExprScopes(ctx, value, scope))
                    {
                        return false;
                    }
                }

                return true;
            case ImportStmt importStmt:
                foreach (var specifier in importStmt.Specifiers)
                {
                    var declName = specifier.Alias ?? specifier.Name;
                    BindImportName(ctx, pkg, scope, declName, importStmt.ID);
                }

                if (importStmt.Alias != null)
                {
                    BindImportName(ctx, pkg, scope, importStmt.Alias, importStmt.ID);
                }
                return true;
            case ExportStmt exportStmt:
                return BindDeclScopes(ctx, exportStmt.Declaration, scope);

            case MatchStmt matchStmt:
            {
                pkg.Scopes.BindNode(matchStmt.ID, scope);
                if (!BindExprScopes(ctx, matchStmt.Scrutinee, scope)) return false;
                foreach (var arm in matchStmt.Arms)
                {
                    var armScope = pkg.Scopes.NewScope(scope);
                    if (arm.Pattern.Kind == MatchPatternKind.TypeBinding && arm.Pattern.Binding != null)
                    {
                        DeclareSymbol(ctx, armScope, arm.Pattern.Binding.Name, SymbolKind.Variable, matchStmt.ID);
                        BindPatternBinding(pkg, armScope, arm.Pattern.Binding);
                    }
                    if (arm.Pattern.ValueExpr != null && !BindExprScopes(ctx, arm.Pattern.ValueExpr, scope)) return false;
                    if (arm.Guard != null && !BindExprScopes(ctx, arm.Guard, armScope)) return false;
                    if (!BindStmtListScopes(ctx, arm.Body, armScope)) return false;
                }
                return true;
            }

            case ContinueStmt:
                return true;
            case DeferStmt deferStmt:
                if (deferStmt.Call != null) return BindExprScopes(ctx, deferStmt.Call, scope);
                if (deferStmt.Block != null) return BindStmtListScopes(ctx, deferStmt.Block, pkg.Scopes.NewScope(scope));
                return true;
            case GuardStmt guardStmt:
                if (!BindExprScopes(ctx, guardStmt.Condition, scope)) return false;
                if (guardStmt.ElseExpr != null) return BindExprScopes(ctx, guardStmt.ElseExpr, scope);
                return true;

            default:
                throw new InvalidOperationException($"Unsupported statement type: {stmt.GetType().Name}");
        }
    }

    private bool BindDeclScopes(PassContext ctx, Decl decl, ScopeID scope)
    {
        var pkg = ctx.Pkg!;
        switch (decl)
        {
            case FunctionDecl funcDecl:
            {
                if (funcDecl.NamePath.Count == 1 && funcDecl.MethodName == null)
                {
                    if (funcDecl.IsAsync)
                        DeclareSymbol(ctx, scope, funcDecl.NamePath[0].Name, SymbolKind.Function, funcDecl.ID, funcDecl.NamePath[0].Span, SymbolFlags.Async);
                    else
                        DeclareSymbol(ctx, scope, funcDecl.NamePath[0].Name, SymbolKind.Function, funcDecl.ID, funcDecl.NamePath[0].Span);
                    StampSide(ctx, scope, funcDecl.NamePath[0].Name, funcDecl.Annotations);
                }

                var funcScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(funcDecl.ID, funcScope);
                DeclareTypeParams(ctx, funcScope, funcDecl.TypeParams);
                foreach (var param in funcDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, funcScope);
                    DeclareSymbol(ctx, funcScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, funcScope))
                        return false;
                }

                if (funcDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(funcDecl.ReturnType.ID, funcScope);
                }

                if (!BindStmtListScopes(ctx, funcDecl.Body, funcScope))
                {
                    return false;
                }

                if (funcDecl.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, funcDecl.ReturnStmt, funcScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case LocalFunctionDecl localFuncDecl:
            {
                if (localFuncDecl.IsAsync)
                    DeclareSymbol(ctx, scope, localFuncDecl.Name.Name, SymbolKind.Function, localFuncDecl.ID, localFuncDecl.Name.Span, SymbolFlags.Async);
                else
                    DeclareSymbol(ctx, scope, localFuncDecl.Name.Name, SymbolKind.Function, localFuncDecl.ID, localFuncDecl.Name.Span);
                StampSide(ctx, scope, localFuncDecl.Name.Name, localFuncDecl.Annotations);

                var localFuncScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(localFuncDecl.ID, localFuncScope);
                DeclareTypeParams(ctx, localFuncScope, localFuncDecl.TypeParams);
                foreach (var param in localFuncDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, localFuncScope);
                    DeclareSymbol(ctx, localFuncScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, localFuncScope))
                        return false;
                }

                if (localFuncDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(localFuncDecl.ReturnType.ID, localFuncScope);
                }

                if (!BindStmtListScopes(ctx, localFuncDecl.Body, localFuncScope))
                {
                    return false;
                }

                if (localFuncDecl.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, localFuncDecl.ReturnStmt, localFuncScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case LocalDecl localDecl:
            {
                pkg.Scopes.BindNode(localDecl.ID, scope);

                foreach (var expr in localDecl.Values)
                {
                    if (!BindExprScopes(ctx, expr, scope))
                    {
                        return false;
                    }
                }

                var immutableDefault = ctx.Config.Rules.ImmutableDefault;
                var deepFreeze = ctx.Config.Rules.DeepFreeze;

                foreach (var variable in localDecl.Variables)
                {
                    var flags = new List<SymbolFlags>();
                    if (localDecl.IsMutable || variable.Attribute == "mutable")
                    {
                        flags.Add(SymbolFlags.Mutable);
                    }
                    else if (immutableDefault)
                    {
                        flags.Add(SymbolFlags.Immutable);
                        if (deepFreeze) flags.Add(SymbolFlags.DeepFreeze);
                    }

                    if (variable.Attribute == "const")
                        flags.Add(SymbolFlags.Const);

                    DeclareSymbol(ctx, scope, variable.Name.Name, SymbolKind.Variable, localDecl.ID, variable.Name.Span, flags.ToArray());
                    StampSide(ctx, scope, variable.Name.Name, localDecl.Annotations);
                }

                return true;
            }
            case DeclareFunctionDecl declareFuncDecl:
            {
                if (declareFuncDecl.NamePath.Count == 1 && declareFuncDecl.MethodName == null)
                {
                    if (declareFuncDecl.IsAsync)
                        DeclareSymbol(ctx, scope, declareFuncDecl.NamePath[0].Name, SymbolKind.Function, declareFuncDecl.ID, declareFuncDecl.NamePath[0].Span, SymbolFlags.Async, SymbolFlags.External);
                    else
                        DeclareSymbol(ctx, scope, declareFuncDecl.NamePath[0].Name, SymbolKind.Function, declareFuncDecl.ID, declareFuncDecl.NamePath[0].Span, SymbolFlags.External);
                    StampSide(ctx, scope, declareFuncDecl.NamePath[0].Name, declareFuncDecl.Annotations);
                }

                var declareFuncScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(declareFuncDecl.ID, declareFuncScope);
                DeclareTypeParams(ctx, declareFuncScope, declareFuncDecl.TypeParams);
                foreach (var param in declareFuncDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, declareFuncScope);
                    DeclareSymbol(ctx, declareFuncScope, param.Name.Name, SymbolKind.Variable, param.ID);
                }

                if (declareFuncDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(declareFuncDecl.ReturnType.ID, declareFuncScope);
                }

                return true;
            }
            case DeclareVariableDecl declareVarDecl:
            {
                DeclareSymbol(ctx, scope, declareVarDecl.Name.Name, SymbolKind.Variable, declareVarDecl.ID, declareVarDecl.Name.Span, SymbolFlags.External);
                StampSide(ctx, scope, declareVarDecl.Name.Name, declareVarDecl.Annotations);
                pkg.Scopes.BindNode(declareVarDecl.ID, scope);
                return true;
            }
            case DeclareModuleDecl declareModuleDecl:
            {
                DeclareSymbol(ctx, scope, declareModuleDecl.ModuleName.Name, SymbolKind.Variable, declareModuleDecl.ID, declareModuleDecl.ModuleName.Span, SymbolFlags.External);
                StampSide(ctx, scope, declareModuleDecl.ModuleName.Name, declareModuleDecl.Annotations);
                var moduleScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(declareModuleDecl.ID, moduleScope);

                foreach (var member in declareModuleDecl.Members)
                {
                    if (!BindDeclScopes(ctx, member, moduleScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case EnumDecl enumDecl:
            {
                DeclareSymbol(ctx, scope, enumDecl.Name.Name, SymbolKind.Enum, enumDecl.ID, enumDecl.Name.Span);
                StampSide(ctx, scope, enumDecl.Name.Name, enumDecl.Annotations);
                pkg.Scopes.BindNode(enumDecl.ID, scope);
                foreach (var member in enumDecl.Members)
                {
                    if (member.Value != null && !BindExprScopes(ctx, member.Value, scope))
                    {
                        return false;
                    }
                }
                return true;
            }
            case ClassDecl classDecl:
            {
                DeclareSymbol(ctx, scope, classDecl.Name.Name, SymbolKind.Class, classDecl.ID, classDecl.Name.Span);
                StampSide(ctx, scope, classDecl.Name.Name, classDecl.Annotations);
                var classScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(classDecl.ID, classScope);
                DeclareTypeParams(ctx, classScope, classDecl.TypeParams);

                if (classDecl.Constructor != null)
                {
                    var ctorScope = pkg.Scopes.NewScope(classScope);
                    DeclareSymbol(ctx, ctorScope, "self", SymbolKind.Variable, classDecl.ID);
                    foreach (var param in classDecl.Constructor.Parameters)
                    {
                        pkg.Scopes.BindNode(param.ID, ctorScope);
                        DeclareSymbol(ctx, ctorScope, param.Name.Name, SymbolKind.Variable, param.ID);
                        if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, ctorScope))
                            return false;
                    }
                    if (!BindStmtListScopes(ctx, classDecl.Constructor.Body, ctorScope))
                        return false;
                    if (classDecl.Constructor.ReturnStmt != null && !BindStmtScopes(ctx, classDecl.Constructor.ReturnStmt, ctorScope))
                        return false;
                }

                foreach (var method in classDecl.Methods)
                {
                    if (method.IsLocal) continue;
                    var methodScope = pkg.Scopes.NewScope(classScope);
                    DeclareTypeParams(ctx, methodScope, method.TypeParams);
                    if (!method.IsStatic)
                        DeclareSymbol(ctx, methodScope, "self", SymbolKind.Variable, classDecl.ID);
                    foreach (var param in method.Parameters)
                    {
                        pkg.Scopes.BindNode(param.ID, methodScope);
                        DeclareSymbol(ctx, methodScope, param.Name.Name, SymbolKind.Variable, param.ID);
                        if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, methodScope))
                            return false;
                    }
                    if (!BindStmtListScopes(ctx, method.Body, methodScope))
                        return false;
                    if (method.ReturnStmt != null && !BindStmtScopes(ctx, method.ReturnStmt, methodScope))
                        return false;
                }

                foreach (var accessor in classDecl.Accessors)
                {
                    var accScope = pkg.Scopes.NewScope(classScope);
                    DeclareSymbol(ctx, accScope, "self", SymbolKind.Variable, classDecl.ID);
                    foreach (var param in accessor.Parameters)
                    {
                        pkg.Scopes.BindNode(param.ID, accScope);
                        DeclareSymbol(ctx, accScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    }
                    if (!BindStmtListScopes(ctx, accessor.Body, accScope))
                        return false;
                    if (accessor.ReturnStmt != null && !BindStmtScopes(ctx, accessor.ReturnStmt, accScope))
                        return false;
                }

                foreach (var field in classDecl.Fields)
                {
                    if (field.DefaultValue != null && !BindExprScopes(ctx, field.DefaultValue, classScope))
                        return false;
                }

                return true;
            }
            case InterfaceDecl interfaceDecl:
            {
                DeclareSymbol(ctx, scope, interfaceDecl.Name.Name, SymbolKind.Interface, interfaceDecl.ID, interfaceDecl.Name.Span);
                StampSide(ctx, scope, interfaceDecl.Name.Name, interfaceDecl.Annotations);
                var ifaceScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(interfaceDecl.ID, ifaceScope);
                DeclareTypeParams(ctx, ifaceScope, interfaceDecl.TypeParams);

                // Default methods carry a body: give each its own scope with an implicit
                // `self` (typed as the interface in InferTypes) and its parameters, then bind
                // the body — the same shape as a class instance method.
                foreach (var method in interfaceDecl.Methods)
                {
                    if (!method.IsDefault) continue;
                    var methodScope = pkg.Scopes.NewScope(ifaceScope);
                    DeclareTypeParams(ctx, methodScope, method.TypeParams);
                    DeclareSymbol(ctx, methodScope, "self", SymbolKind.Variable, interfaceDecl.ID);
                    foreach (var param in method.Parameters)
                    {
                        pkg.Scopes.BindNode(param.ID, methodScope);
                        DeclareSymbol(ctx, methodScope, param.Name.Name, SymbolKind.Variable, param.ID);
                        if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, methodScope))
                            return false;
                    }
                    if (!BindStmtListScopes(ctx, method.Body!, methodScope))
                        return false;
                    if (method.ReturnStmt != null && !BindStmtScopes(ctx, method.ReturnStmt, methodScope))
                        return false;
                }
                return true;
            }

            case ExtendDecl extendDecl:
            {
                var extScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(extendDecl.ID, extScope);
                foreach (var method in extendDecl.Methods)
                {
                    var methodScope = pkg.Scopes.NewScope(extScope);
                    DeclareTypeParams(ctx, methodScope, method.TypeParams);
                    DeclareSymbol(ctx, methodScope, "self", SymbolKind.Variable, extendDecl.ID);
                    foreach (var param in method.Parameters)
                    {
                        pkg.Scopes.BindNode(param.ID, methodScope);
                        DeclareSymbol(ctx, methodScope, param.Name.Name, SymbolKind.Variable, param.ID);
                        if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, methodScope))
                            return false;
                    }
                    if (!BindStmtListScopes(ctx, method.Body, methodScope))
                        return false;
                    if (method.ReturnStmt != null && !BindStmtScopes(ctx, method.ReturnStmt, methodScope))
                        return false;
                }
                return true;
            }

            default:
                throw new InvalidOperationException($"Unsupported declaration type: {decl.GetType().Name}");
        }
    }

    private bool BindExprScopes(PassContext ctx, Expr expr, ScopeID scope)
    {
        if (expr == null)
        {
            return true;
        }

        var pkg = ctx.Pkg!;
        pkg.Scopes.BindNode(expr.ID, scope);

        switch (expr)
        {
            case NilLiteralExpr:
            case BoolLiteralExpr:
            case NumberLiteralExpr:
            case StringLiteralExpr:
            case VarargExpr:
            case NameExpr:
                return true;
            case FunctionDefExpr funcDef:
                var funcScope = pkg.Scopes.NewScope(scope);
                foreach (var param in funcDef.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, funcScope);
                    DeclareSymbol(ctx, funcScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, funcScope))
                        return false;
                }

                if (funcDef.ReturnType != null)
                {
                    pkg.Scopes.BindNode(funcDef.ReturnType.ID, funcScope);
                }

                foreach (var stmt in funcDef.Body)
                {
                    if (!BindStmtScopes(ctx, stmt, funcScope))
                    {
                        return false;
                    }
                }

                if (funcDef.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, funcDef.ReturnStmt, funcScope))
                    {
                        return false;
                    }
                }

                return true;
            case BinaryExpr binary:
                return BindExprScopes(ctx, binary.Left, scope) && BindExprScopes(ctx, binary.Right, scope);
            case UnaryExpr unary:
                return BindExprScopes(ctx, unary.Operand, scope);
            case ParenExpr paren:
                return BindExprScopes(ctx, paren.Inner, scope);
            case DotAccessExpr dotAccess:
                return BindExprScopes(ctx, dotAccess.Object, scope);
            case IndexAccessExpr indexAccess:
                return BindExprScopes(ctx, indexAccess.Object, scope) && BindExprScopes(ctx, indexAccess.Index, scope);
            case FunctionCallExpr funcCall:
                if (!BindExprScopes(ctx, funcCall.Callee, scope))
                {
                    return false;
                }

                foreach (var arg in funcCall.Arguments)
                {
                    if (!BindExprScopes(ctx, arg, scope))
                    {
                        return false;
                    }
                }

                return true;
            case MethodCallExpr methodCall:
                if (!BindExprScopes(ctx, methodCall.Object, scope))
                {
                    return false;
                }

                foreach (var arg in methodCall.Arguments)
                {
                    if (!BindExprScopes(ctx, arg, scope))
                    {
                        return false;
                    }
                }

                return true;
            case InterpolatedStringExpr interpolatedString:
                foreach (var part in interpolatedString.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        if (!BindExprScopes(ctx, exprPart.Expression, scope))
                        {
                            return false;
                        }
                    }
                }

                return true;
            case NonNilAssertExpr nonNilAssert:
                return BindExprScopes(ctx, nonNilAssert.Inner, scope);
            case IncDecExpr incDec:
                return BindExprScopes(ctx, incDec.Target, scope);
            case TypeCheckExpr typeCheck:
                pkg.Scopes.BindNode(typeCheck.TargetType.ID, scope);
                return BindExprScopes(ctx, typeCheck.Inner, scope);
            case TypeCastExpr typeCast:
                pkg.Scopes.BindNode(typeCast.TargetType.ID, scope);
                return BindExprScopes(ctx, typeCast.Inner, scope);
            case TypeOfExpr typeOf:
                return BindExprScopes(ctx, typeOf.Inner, scope);
            case InstanceOfExpr instOf:
                return BindExprScopes(ctx, instOf.Inner, scope);
            case TableConstructorExpr tableConstructor:
                foreach (var field in tableConstructor.Fields)
                {
                    if (field.Key != null)
                    {
                        if (!BindExprScopes(ctx, field.Key, scope))
                        {
                            return false;
                        }
                    }

                    if (!BindExprScopes(ctx, field.Value, scope))
                    {
                        return false;
                    }
                }

                return true;

            case MatchExpr matchExpr:
            {
                pkg.Scopes.BindNode(matchExpr.ID, scope);
                if (!BindExprScopes(ctx, matchExpr.Scrutinee, scope)) return false;
                foreach (var arm in matchExpr.Arms)
                {
                    var armScope = pkg.Scopes.NewScope(scope);
                    if (arm.Pattern.Kind == MatchPatternKind.TypeBinding && arm.Pattern.Binding != null)
                    {
                        DeclareSymbol(ctx, armScope, arm.Pattern.Binding.Name, SymbolKind.Variable, matchExpr.ID);
                        BindPatternBinding(pkg, armScope, arm.Pattern.Binding);
                    }
                    if (arm.Pattern.ValueExpr != null && !BindExprScopes(ctx, arm.Pattern.ValueExpr, scope)) return false;
                    if (arm.Guard != null && !BindExprScopes(ctx, arm.Guard, armScope)) return false;
                    if (!BindExprScopes(ctx, arm.Value, armScope)) return false;
                }
                return true;
            }

            case AwaitExpr awaitExpr:
                return BindExprScopes(ctx, awaitExpr.Expression, scope);

            case NewExpr newExpr:
                foreach (var arg in newExpr.Arguments)
                    if (!BindExprScopes(ctx, arg, scope)) return false;
                return true;

            case SuperCallExpr superCall:
                foreach (var arg in superCall.Arguments)
                    if (!BindExprScopes(ctx, arg, scope)) return false;
                return true;

            default:
                throw new InvalidOperationException($"Unsupported expression type: {expr.GetType().Name}");
        }
    }

    /// <summary>
    /// Binds an import specifier into <paramref name="scope"/>. If a same-named
    /// symbol already exists in the current scope (typically a class / interface
    /// / enum from a <c>.d.neb</c> file that was loaded by
    /// <see cref="ResolveLibsPass"/>), the import is treated as an alias of that
    /// existing symbol — its <see cref="NameRef.Sym"/> is set to the existing
    /// <see cref="SymID"/>, and no new symbol is created. This lets
    /// <c>import { Vec2 }</c> participate in <c>: Vec2</c> type lookups without
    /// triggering a redeclaration. Otherwise falls back to declaring a fresh
    /// <see cref="SymbolKind.Variable"/> bound to the import statement, which
    /// later passes can fill in via type propagation.
    /// </summary>
    private static void BindImportName(PassContext ctx, PackageContext pkg, ScopeID scope, NameRef nameRef, NodeID importNode)
    {
        if (pkg.Scopes.LookupOnlyCurrent(scope, nameRef.Name, out var existing))
        {
            nameRef.Sym = existing;
            return;
        }
        DeclareSymbol(ctx, scope, nameRef.Name, SymbolKind.Variable, importNode, nameRef.Span);
    }

    /// <summary>
    /// Points a match arm's type-binding name at the symbol just declared for it. Without this the
    /// name reference carries no symbol, so anything resolving through it falls back to the source
    /// spelling: with mangling on, the arm declared the raw name while its body read the mangled
    /// one.
    /// </summary>
    private static void BindPatternBinding(PackageContext pkg, ScopeID armScope, NameRef binding)
    {
        if (pkg.Scopes.LookupOnlyCurrent(armScope, binding.Name, out var sym))
            binding.Sym = sym;
    }

    private static void DeclareSymbol(PassContext ctx, ScopeID scope, string name, SymbolKind kind, NodeID decl, params SymbolFlags[] flags)
    {
        DeclareSymbol(ctx, scope, name, kind, decl, null, flags);
    }

    private static void DeclareSymbol(PassContext ctx, ScopeID scope, string name, SymbolKind kind, NodeID decl, TextSpan? span, params SymbolFlags[] flags)
    {
        var pkg = ctx.Pkg!;
        var symId = pkg.Syms.NewSymbol(kind, name, scope, TypID.Invalid, decl, flags);
        pkg.Scopes.DeclareSymbol(scope, name, symId, pkg.Syms, span);
    }

    /// <summary>
    /// Reads <c>@side(...)</c> annotations off a declaration and stamps the
    /// resolved <see cref="Side"/> mask on the symbol just declared in
    /// <paramref name="scope"/>. Falls back to <see cref="Side.All"/> when
    /// unannotated, so unmarked decls remain accessible everywhere.
    /// </summary>
    private static void StampSide(PassContext ctx, ScopeID scope, string name, List<Annotation> annotations)
    {
        if (annotations == null || annotations.Count == 0) return;
        var pkg = ctx.Pkg!;
        if (!pkg.Scopes.LookupOnlyCurrent(scope, name, out var symId)) return;
        if (!pkg.Syms.GetByID(symId, out var sym)) return;
        sym.Side = BuiltinAnnotations.ExtractSide(annotations, (ann, badName) =>
        {
            if (!string.IsNullOrEmpty(badName))
                ctx.Diag.Report(ann.Span, DiagnosticCode.ErrUnknownSideName, badName);
        });
    }

    private static void DeclareTypeParams(PassContext ctx, ScopeID scope, List<TypeParamDef> typeParams)
    {
        if (typeParams.Count == 0) return;
        var seen = new HashSet<string>();
        foreach (var tp in typeParams)
        {
            if (!seen.Add(tp.Name.Name))
            {
                ctx.Diag.Report(tp.Span, Nebra.Diagnostics.DiagnosticCode.ErrDuplicateTypeParam, tp.Name.Name);
                continue;
            }
            DeclareSymbol(ctx, scope, tp.Name.Name, SymbolKind.TypeParam, tp.ID);
        }
    }
}
