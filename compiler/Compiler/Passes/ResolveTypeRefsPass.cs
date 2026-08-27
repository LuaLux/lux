using Nebra.IR;
using Type = Nebra.IR.Type;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The resolve type refs pass is responsible for resolving type references in the source code. It binds their
/// type references to their respective type IDs.
/// </summary>
public class ResolveTypeRefsPass() : Pass(PassName, PassScope.PerBuild)
{
    public const string PassName = "ResolveTypeRefs";

    private PassContext _ctx = null!;
    private PackageContext _pkg = null!;
    private ScopeID _currentScope = ScopeID.Invalid;

    public override bool Run(PassContext context)
    {
        _ctx = context;

        foreach (var pkg in context.Pkgs)
        {
            _pkg = pkg;
            foreach (var f in pkg.Files)
            {
                DeclareEnumTypes(pkg, f.Hir.Body);
                DeclareClassAndInterfaceTypes(pkg, f.Hir.Body);
            }
        }

        foreach (var pkg in context.Pkgs)
            foreach (var f in pkg.Files)
                ResolveImportsPass.PropagateImportTypes(context, pkg, f);

        foreach (var pkg in context.Pkgs)
        {
            _pkg = pkg;
            _currentScope = pkg.Root;
            foreach (var f in pkg.Files)
            {
                ResolveStmtListTypes(pkg.Types, f.Hir.Body);

                if (f.Hir.Return != null)
                {
                    ResolveStmtTypes(pkg.Types, f.Hir.Return);
                }
            }
        }

        return true;
    }

    private ScopeID ScopeOfDecl(NodeID id)
    {
        return _pkg.Scopes.EnclosingScope(id, out var s) ? s : _pkg.Root;
    }

    /// <summary>
    /// The reflection namespace for a package: the root project uses its configured name; every other
    /// package (dependency) uses its directory leaf, so same-named types across packages get distinct ids.
    /// </summary>
    private string ReflectNamespace(PackageContext pkg)
    {
        var leaf = System.IO.Path.GetFileNameWithoutExtension(pkg.Path.TrimEnd('/', '\\'));
        var isRoot = ReferenceEquals(pkg, _ctx.Pkgs.FirstOrDefault());
        return isRoot ? _ctx.Config.Name ?? (string.IsNullOrEmpty(leaf) ? "main" : leaf)
                      : string.IsNullOrEmpty(leaf) ? "main" : leaf;
    }

    /// <summary>
    /// Pre-pass: walk top-level statements and create the EnumType for every EnumDecl so that NamedTypeRefs
    /// referring to enums can be resolved later regardless of declaration order.
    /// </summary>
    private void DeclareEnumTypes(PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            EnumDecl? enumDecl = stmt switch
            {
                EnumDecl ed => ed,
                ExportStmt es when es.Declaration is EnumDecl ed2 => ed2,
                _ => null
            };
            if (enumDecl == null) continue;
            DeclareEnumType(pkg, enumDecl);
        }
    }

    private void DeclareClassAndInterfaceTypes(PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case ClassDecl cd:
                    DeclareClassType(pkg, cd);
                    break;
                case InterfaceDecl id:
                    DeclareInterfaceType(pkg, id);
                    break;
                case ExportStmt es:
                    if (es.Declaration is ClassDecl cd2) DeclareClassType(pkg, cd2);
                    else if (es.Declaration is InterfaceDecl id2) DeclareInterfaceType(pkg, id2);
                    break;
                case DeclareModuleDecl dmd:
                    foreach (var member in dmd.Members)
                    {
                        if (member is ClassDecl cd3) DeclareClassType(pkg, cd3);
                        else if (member is InterfaceDecl id3) DeclareInterfaceType(pkg, id3);
                    }
                    break;
            }
        }
    }

    private void DeclareClassType(PackageContext pkg, ClassDecl decl)
    {
        if (decl.Name.Sym == SymID.Invalid) return;
        if (!pkg.Syms.GetByID(decl.Name.Sym, out var sym)) return;
        if (sym.Type != TypID.Invalid) return;

        var classType = pkg.Types.ClassOf(decl.Name.Name, isAbstract: decl.IsAbstract);
        classType.ReflectionId = $"{ReflectNamespace(pkg)}::{decl.Name.Name}";
        pkg.Syms.SetType(decl.Name.Sym, classType.ID);
        MaterializeTypeParams(pkg, decl.TypeParams, $"class:{decl.Name.Name}", classType.TypeParams, ScopeOfDecl(decl.ID));
    }

    private void DeclareInterfaceType(PackageContext pkg, InterfaceDecl decl)
    {
        if (decl.Name.Sym == SymID.Invalid) return;
        if (!pkg.Syms.GetByID(decl.Name.Sym, out var sym)) return;
        if (sym.Type != TypID.Invalid) return;

        var interfaceType = pkg.Types.InterfaceOf(decl.Name.Name);
        interfaceType.ReflectionId = $"{ReflectNamespace(pkg)}::{decl.Name.Name}";
        pkg.Syms.SetType(decl.Name.Sym, interfaceType.ID);
        MaterializeTypeParams(pkg, decl.TypeParams, $"iface:{decl.Name.Name}", interfaceType.TypeParams, ScopeOfDecl(decl.ID));
    }

    private void MaterializeFunctionTypeParams(PackageContext pkg, List<TypeParamDef> defs, string ownerKey, ScopeID declScope)
    {
        var sink = new List<TypeParameterType>();
        MaterializeTypeParams(pkg, defs, ownerKey, sink, declScope);
    }

    private void MaterializeTypeParams(PackageContext pkg, List<TypeParamDef> defs, string ownerKey, List<TypeParameterType> sink, ScopeID declScope)
    {
        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            var tp = pkg.Types.TypeParamOf(def.Name.Name, ownerKey, i);
            def.ResolvedType = tp.ID;
            sink.Add(tp);

            if (declScope != ScopeID.Invalid
                && pkg.Scopes.LookupOnlyCurrent(declScope, def.Name.Name, out var tpSymId)
                && pkg.Syms.GetByID(tpSymId, out var tpSym)
                && tpSym.Type == TypID.Invalid)
            {
                pkg.Syms.SetType(tpSymId, tp.ID);
            }
        }
    }

    /// <summary>
    /// After type-param bounds (<c>extends</c> / <c>implements</c>) have had their
    /// <see cref="TypeRef.ResolvedType"/> filled in, copy those resolved IDs onto
    /// the corresponding <see cref="TypeParameterType"/> so later passes can read
    /// the bounds from the type universe (used for assignability and the
    /// generic-constraint validator).
    /// </summary>
    private static void PopulateTypeParamBounds(TypeTable tt, List<TypeParamDef> defs)
    {
        foreach (var def in defs)
        {
            if (def.ResolvedType == TypID.Invalid) continue;
            if (!tt.GetByID(def.ResolvedType, out var t) || t is not TypeParameterType tpType) continue;

            if (def.ExtendsBound != null && def.ExtendsBound.ResolvedType != TypID.Invalid)
                tpType.ExtendsBound = def.ExtendsBound.ResolvedType;

            foreach (var ib in def.ImplementsBounds)
            {
                if (ib.ResolvedType == TypID.Invalid) continue;
                if (!tpType.ImplementsBounds.Contains(ib.ResolvedType))
                    tpType.ImplementsBounds.Add(ib.ResolvedType);
            }
        }
    }

    private void DeclareEnumType(PackageContext pkg, EnumDecl decl)
    {
        if (decl.Name.Sym == SymID.Invalid) return;
        if (!pkg.Syms.GetByID(decl.Name.Sym, out var sym)) return;
        if (sym.Type != TypID.Invalid) return;

        var hasStringValues = decl.Members.Any(m => m.Value is StringLiteralExpr);
        var baseType = hasStringValues ? pkg.Types.PrimString : pkg.Types.PrimNumber;
        var members = new List<EnumType.Member>();
        long autoIndex = 0;
        foreach (var m in decl.Members)
        {
            object? value = m.Value switch
            {
                NumberLiteralExpr nl => nl.Raw,
                StringLiteralExpr sl => sl.Value,
                _ => null
            };
            if (value == null && !hasStringValues)
            {
                value = autoIndex.ToString();
            }
            if (value is string s && long.TryParse(s, out var parsed))
            {
                autoIndex = parsed + 1;
            }
            else
            {
                autoIndex++;
            }
            members.Add(new EnumType.Member(m.Name.Name, value));
        }

        var enumType = pkg.Types.EnumOf(decl.Name.Name, members, baseType);
        enumType.ReflectionId = $"{ReflectNamespace(pkg)}::{decl.Name.Name}";
        pkg.Syms.SetType(decl.Name.Sym, enumType.ID);
    }
    
    private void ResolveStmtListTypes(TypeTable tt, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            ResolveStmtTypes(tt, stmt);
        }
    }
    
    private void ResolveStmtTypes(TypeTable tt, Stmt stmt)
    {
        if (stmt == null) return;
        switch (stmt)
        {
            case Decl decl:
                ResolveDeclTypes(tt, decl);
                break;
            case AssignStmt assignStmt:
                foreach (var target in assignStmt.Targets)
                {
                    ResolveExprTypes(tt, target);
                }

                foreach (var value in assignStmt.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                break;
            case ExprStmt exprStmt:
                ResolveExprTypes(tt, exprStmt.Expression);
                break;
            case LabelStmt:
            case BreakStmt:
            case ContinueStmt:
            case GotoStmt:
                break;
            case DoBlockStmt doBlockStmt:
                ResolveStmtListTypes(tt, doBlockStmt.Body);
                break;
            case WhileStmt whileStmt:
                ResolveExprTypes(tt, whileStmt.Condition);
                ResolveStmtListTypes(tt, whileStmt.Body);
                break;
            case RepeatStmt repeatStmt:
                ResolveStmtListTypes(tt, repeatStmt.Body);
                ResolveExprTypes(tt, repeatStmt.Condition);
                break;
            case IfStmt ifStmt:
                ResolveExprTypes(tt, ifStmt.Condition);
                ResolveStmtListTypes(tt, ifStmt.Body);
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    ResolveExprTypes(tt, elseIf.Condition);
                    ResolveStmtListTypes(tt, elseIf.Body);
                }
                
                if (ifStmt.ElseBody != null)
                {
                    ResolveStmtListTypes(tt, ifStmt.ElseBody);
                }
                
                break;
            case NumericForStmt numericForStmt:
                ResolveExprTypes(tt, numericForStmt.Start);
                ResolveExprTypes(tt, numericForStmt.Limit);
                if (numericForStmt.Step != null)
                {
                    ResolveExprTypes(tt, numericForStmt.Step);
                }
                ResolveStmtListTypes(tt, numericForStmt.Body);
                break;
            case GenericForStmt genericForStmt:
                foreach (var iterator in genericForStmt.Iterators)
                {
                    ResolveExprTypes(tt, iterator);
                }
                ResolveStmtListTypes(tt, genericForStmt.Body);
                break;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                break;
            case ImportStmt:
                break;
            case ExportStmt exportStmt:
                ResolveDeclTypes(tt, exportStmt.Declaration);
                break;
            case MatchStmt matchStmt:
                ResolveExprTypes(tt, matchStmt.Scrutinee);
                foreach (var arm in matchStmt.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) ResolveExprTypes(tt, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) ResolveTypeRef(tt, arm.Pattern.TypeRef);
                    if (arm.Guard != null) ResolveExprTypes(tt, arm.Guard);
                    ResolveStmtListTypes(tt, arm.Body);
                }
                break;
            case DeferStmt ds:
                if (ds.Call != null) ResolveExprTypes(tt, ds.Call);
                if (ds.Block != null) ResolveStmtListTypes(tt, ds.Block);
                break;
            case GuardStmt gs:
                ResolveExprTypes(tt, gs.Condition);
                if (gs.ElseExpr != null) ResolveExprTypes(tt, gs.ElseExpr);
                break;
            default:
                throw new InvalidOperationException($"Unknown statement kind: {stmt.GetType().Name}");
        }
    }

    private void ResolveDeclTypes(TypeTable tt, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl functionDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(functionDecl.ID);
                MaterializeFunctionTypeParams(_pkg, functionDecl.TypeParams,
                    $"fn:{string.Join(".", functionDecl.NamePath.Select(n => n.Name))}@{functionDecl.ID}", _currentScope);
                foreach (var param in functionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (functionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, functionDecl.ReturnType);
                }

                ResolveStmtListTypes(tt, functionDecl.Body);

                if (functionDecl.ReturnStmt != null)
                {
                    ResolveStmtTypes(tt, functionDecl.ReturnStmt);
                }
                _currentScope = prev;
                break;
            }
            case LocalFunctionDecl localFunctionDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(localFunctionDecl.ID);
                MaterializeFunctionTypeParams(_pkg, localFunctionDecl.TypeParams,
                    $"lfn:{localFunctionDecl.Name.Name}@{localFunctionDecl.ID}", _currentScope);
                foreach (var param in localFunctionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (localFunctionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, localFunctionDecl.ReturnType);
                }

                ResolveStmtListTypes(tt, localFunctionDecl.Body);

                if (localFunctionDecl.ReturnStmt != null)
                {
                    ResolveStmtTypes(tt, localFunctionDecl.ReturnStmt);
                }
                _currentScope = prev;
                break;
            }
            case LocalDecl localDecl:
                foreach (var variable in localDecl.Variables)
                {
                    if (variable.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, variable.TypeAnnotation);
                    }
                }
                
                foreach (var value in localDecl.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                
                break;
            case DeclareFunctionDecl declareFunctionDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(declareFunctionDecl.ID);
                MaterializeFunctionTypeParams(_pkg, declareFunctionDecl.TypeParams,
                    $"dfn:{string.Join(".", declareFunctionDecl.NamePath.Select(n => n.Name))}@{declareFunctionDecl.ID}", _currentScope);
                foreach (var param in declareFunctionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (declareFunctionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, declareFunctionDecl.ReturnType);
                }
                _currentScope = prev;
                break;
            }
            case DeclareVariableDecl declareVariableDecl:
                ResolveTypeRef(tt, declareVariableDecl.TypeAnnotation);
                break;
            case DeclareModuleDecl declareModuleDecl:
                foreach (var member in declareModuleDecl.Members)
                {
                    ResolveDeclTypes(tt, member);
                }

                break;
            case EnumDecl enumDecl:
                if (enumDecl.IsDeclare)
                {
                    DeclareEnumType(_pkg, enumDecl);
                }
                foreach (var member in enumDecl.Members)
                {
                    if (member.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, member.TypeAnnotation);
                    }
                }
                break;
            case ClassDecl classDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(classDecl.ID);
                foreach (var tp in classDecl.TypeParams)
                {
                    if (tp.ExtendsBound != null) ResolveTypeRef(tt, tp.ExtendsBound);
                    foreach (var ib in tp.ImplementsBounds) ResolveTypeRef(tt, ib);
                }
                PopulateTypeParamBounds(tt, classDecl.TypeParams);
                foreach (var field in classDecl.Fields)
                {
                    if (field.TypeAnnotation != null) ResolveTypeRef(tt, field.TypeAnnotation);
                    if (field.DefaultValue != null) ResolveExprTypes(tt, field.DefaultValue);
                }
                foreach (var method in classDecl.Methods)
                {
                    foreach (var p in method.Parameters)
                    {
                        if (p.TypeAnnotation != null) ResolveTypeRef(tt, p.TypeAnnotation);
                    }
                    if (method.ReturnType != null) ResolveTypeRef(tt, method.ReturnType);
                    ResolveStmtListTypes(tt, method.Body);
                    if (method.ReturnStmt != null) ResolveStmtTypes(tt, method.ReturnStmt);
                }
                if (classDecl.Constructor != null)
                {
                    foreach (var p in classDecl.Constructor.Parameters)
                    {
                        if (p.TypeAnnotation != null) ResolveTypeRef(tt, p.TypeAnnotation);
                    }
                    ResolveStmtListTypes(tt, classDecl.Constructor.Body);
                    if (classDecl.Constructor.ReturnStmt != null) ResolveStmtTypes(tt, classDecl.Constructor.ReturnStmt);
                }
                foreach (var accessor in classDecl.Accessors)
                {
                    foreach (var p in accessor.Parameters)
                    {
                        if (p.TypeAnnotation != null) ResolveTypeRef(tt, p.TypeAnnotation);
                    }
                    if (accessor.ReturnType != null) ResolveTypeRef(tt, accessor.ReturnType);
                    ResolveStmtListTypes(tt, accessor.Body);
                    if (accessor.ReturnStmt != null) ResolveStmtTypes(tt, accessor.ReturnStmt);
                }
                _currentScope = prev;
                break;
            }
            case InterfaceDecl interfaceDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(interfaceDecl.ID);
                foreach (var tp in interfaceDecl.TypeParams)
                {
                    if (tp.ExtendsBound != null) ResolveTypeRef(tt, tp.ExtendsBound);
                    foreach (var ib in tp.ImplementsBounds) ResolveTypeRef(tt, ib);
                }
                PopulateTypeParamBounds(tt, interfaceDecl.TypeParams);
                foreach (var field in interfaceDecl.Fields)
                {
                    ResolveTypeRef(tt, field.TypeAnnotation);
                }
                foreach (var method in interfaceDecl.Methods)
                {
                    foreach (var p in method.Parameters)
                    {
                        if (p.TypeAnnotation != null) ResolveTypeRef(tt, p.TypeAnnotation);
                    }
                    if (method.ReturnType != null) ResolveTypeRef(tt, method.ReturnType);
                    if (method.IsDefault) ResolveStmtListTypes(tt, method.Body!);
                }
                _currentScope = prev;
                break;
            }
            case ExtendDecl extendDecl:
            {
                var prev = _currentScope;
                _currentScope = ScopeOfDecl(extendDecl.ID);
                // TargetType is null when the `extend <target>` head failed to parse; the method
                // bodies below are still resolved so the user gets full diagnostics, not a crash.
                if (extendDecl.TargetType != null) ResolveTypeRef(tt, extendDecl.TargetType);
                foreach (var method in extendDecl.Methods)
                {
                    foreach (var p in method.Parameters)
                    {
                        if (p.TypeAnnotation != null) ResolveTypeRef(tt, p.TypeAnnotation);
                    }
                    if (method.ReturnType != null) ResolveTypeRef(tt, method.ReturnType);
                    ResolveStmtListTypes(tt, method.Body);
                }
                _currentScope = prev;
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown declaration kind: {decl.GetType().Name}");
        }
    }

    private void ResolveExprTypes(TypeTable tt, Expr expr)
    {
        if (expr == null) return;
        switch (expr)
        {
            case NilLiteralExpr:
            case BoolLiteralExpr:
            case NumberLiteralExpr:
            case StringLiteralExpr:
            case VarargExpr:
                break;
            case FunctionDefExpr functionDefExpr:
                foreach (var param in functionDefExpr.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }
                
                if (functionDefExpr.ReturnType != null)
                {
                    ResolveTypeRef(tt, functionDefExpr.ReturnType);
                }
                
                ResolveStmtListTypes(tt, functionDefExpr.Body);
                break;
            case BinaryExpr binaryExpr:
                ResolveExprTypes(tt, binaryExpr.Left);
                ResolveExprTypes(tt, binaryExpr.Right);
                break;
            case UnaryExpr unaryExpr:
                ResolveExprTypes(tt, unaryExpr.Operand);
                break;
            case NameExpr:
                break;
            case ParenExpr parenExpr:
                ResolveExprTypes(tt, parenExpr.Inner);
                break;
            case DotAccessExpr dotAccessExpr:
                ResolveExprTypes(tt, dotAccessExpr.Object);
                break;
            case IndexAccessExpr indexAccessExpr:
                ResolveExprTypes(tt, indexAccessExpr.Object);
                ResolveExprTypes(tt, indexAccessExpr.Index);
                break;
            case FunctionCallExpr functionCallExpr:
                ResolveExprTypes(tt, functionCallExpr.Callee);
                
                foreach (var arg in functionCallExpr.Arguments)
                {
                    ResolveExprTypes(tt, arg);
                }
                
                break;
            case MethodCallExpr methodCallExpr:
                ResolveExprTypes(tt, methodCallExpr.Object);
                
                foreach (var arg in methodCallExpr.Arguments)
                {
                    ResolveExprTypes(tt, arg);
                }
                
                break;
            case InterpolatedStringExpr interpolatedStringExpr:
                foreach (var part in interpolatedStringExpr.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        ResolveExprTypes(tt, exprPart.Expression);
                    }
                }
                break;
            case NonNilAssertExpr nonNilAssert:
                ResolveExprTypes(tt, nonNilAssert.Inner);
                break;
            case IncDecExpr incDec:
                ResolveExprTypes(tt, incDec.Target);
                break;
            case TypeCheckExpr typeCheck:
                ResolveExprTypes(tt, typeCheck.Inner);
                ResolveTypeRef(tt, typeCheck.TargetType);
                break;
            case TypeCastExpr typeCast:
                ResolveExprTypes(tt, typeCast.Inner);
                ResolveTypeRef(tt, typeCast.TargetType);
                break;
            case TypeOfExpr typeOf:
                ResolveExprTypes(tt, typeOf.Inner);
                break;
            case InstanceOfExpr instOf:
                ResolveExprTypes(tt, instOf.Inner);
                break;
            case TableConstructorExpr tableConstructorExpr:
                foreach (var field in tableConstructorExpr.Fields)
                {
                    if (field.Key != null)
                    {
                        ResolveExprTypes(tt, field.Key);
                    }

                    ResolveExprTypes(tt, field.Value);
                }

                break;
            case AwaitExpr awaitExpr:
                ResolveExprTypes(tt, awaitExpr.Expression);
                break;
            case NewExpr newExpr:
                foreach (var arg in newExpr.Arguments) ResolveExprTypes(tt, arg);
                break;
            case SuperCallExpr superCallExpr:
                foreach (var arg in superCallExpr.Arguments) ResolveExprTypes(tt, arg);
                break;
            case MatchExpr matchExpr:
                ResolveExprTypes(tt, matchExpr.Scrutinee);
                foreach (var arm in matchExpr.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) ResolveExprTypes(tt, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) ResolveTypeRef(tt, arm.Pattern.TypeRef);
                    if (arm.Guard != null) ResolveExprTypes(tt, arm.Guard);
                    ResolveExprTypes(tt, arm.Value);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown expression kind: {expr.GetType().Name}");
        }
    }
    
    private Type ResolveTypeRef(TypeTable tt, TypeRef tr)
    {
        if (tr.ResolvedType != TypID.Invalid)
        {
            tt.GetByID(tr.ResolvedType, out var resolvedType);
            return resolvedType;
        }

        var lookupScope = _currentScope != ScopeID.Invalid ? _currentScope : _pkg.Root;

        if (tr is TypePredicateRef tpr)
        {
            // `param is Type` — resolve the narrowed-to target; the guard's runtime type is boolean.
            ResolveTypeRef(tt, tpr.TargetType);
            tr.ResolvedType = tt.PrimBool.ID;
            return tt.PrimBool;
        }

        if (tr is NamedTypeRef nrt)
        {
            if (_pkg.Scopes.Lookup(lookupScope, nrt.Name.Name, out var symId)
                && _pkg.Syms.GetByID(symId, out var sym)
                && sym.Type != TypID.Invalid)
            {
                tt.GetByID(sym.Type, out var resolved);
                nrt.Name.Sym = symId;
                tr.ResolvedType = sym.Type;
                return resolved;
            }

            _ctx.Diag.Report(tr.Span, Nebra.Diagnostics.DiagnosticCode.ErrUndeclaredSymbol, nrt.Name.Name);
            tr.ResolvedType = tt.PrimAny.ID;
            return tt.PrimAny;
        }

        if (tr is GenericTypeRef gtr)
        {
            if (!_pkg.Scopes.Lookup(lookupScope, gtr.Name.Name, out var gSym)
                || !_pkg.Syms.GetByID(gSym, out var gsym)
                || gsym.Type == TypID.Invalid
                || !tt.GetByID(gsym.Type, out var gDefType))
            {
                _ctx.Diag.Report(tr.Span, Nebra.Diagnostics.DiagnosticCode.ErrUndeclaredSymbol, gtr.Name.Name);
                tr.ResolvedType = tt.PrimAny.ID;
                return tt.PrimAny;
            }

            gtr.Name.Sym = gSym;

            int expectedArity;
            switch (gDefType)
            {
                case ClassType ct: expectedArity = ct.TypeParams.Count; break;
                case InterfaceType it: expectedArity = it.TypeParams.Count; break;
                default:
                    _ctx.Diag.Report(tr.Span, Nebra.Diagnostics.DiagnosticCode.ErrNonGenericTypeArgs, gtr.Name.Name);
                    tr.ResolvedType = gDefType.ID;
                    return gDefType;
            }

            if (expectedArity == 0)
            {
                _ctx.Diag.Report(tr.Span, Nebra.Diagnostics.DiagnosticCode.ErrNonGenericTypeArgs, gtr.Name.Name);
                tr.ResolvedType = gDefType.ID;
                return gDefType;
            }

            if (gtr.Arguments.Count != expectedArity)
            {
                _ctx.Diag.Report(tr.Span, Nebra.Diagnostics.DiagnosticCode.ErrTypeParamArityMismatch,
                    gtr.Name.Name, expectedArity, gtr.Arguments.Count);
            }

            foreach (var argRef in gtr.Arguments)
            {
                switch (argRef)
                {
                    case ConcreteTypeArgRef cta:
                        ResolveTypeRef(tt, cta.Type);
                        break;
                    case WildcardTypeArgRef wta:
                        if (wta.Bound != null) ResolveTypeRef(tt, wta.Bound);
                        break;
                }
            }

            tr.ResolvedType = gDefType.ID;
            return gDefType;
        }

        if (tr is NullableTypeRef nt)
        {
            var inner = ResolveTypeRef(tt, nt.InnerType);
            var unionType = tt.DeclareType(new UnionType([inner, tt.PrimNil]));
            tr.ResolvedType = unionType.ID;
            return unionType;
        }

        Type result;
        switch (tr.Kind)
        {
            case TypeKind.PrimitiveAny:
                result = tt.PrimAny;
                break;
            case TypeKind.PrimitiveNil:
                result = tt.PrimNil;
                break;
            case TypeKind.PrimitiveString:
                result = tt.PrimString;
                break;
            case TypeKind.PrimitiveNumber:
                result = tt.PrimNumber;
                break;
            case TypeKind.PrimitiveBool:
                result = tt.PrimBool;
                break;
            case TypeKind.PrimitiveFunction:
                result = tt.PrimFunction;
                break;
            case TypeKind.PrimitiveThread:
                result = tt.PrimThread;
                break;
            case TypeKind.PrimitiveUserdata:
                result = tt.PrimUserdata;
                break;
            case TypeKind.PrimitiveNever:
                result = tt.PrimNever;
                break;
            case TypeKind.TableArray:
            {
                var et = ResolveTypeRef(tt, ((ArrayTypeRef) tr).ElementType);
                result = tt.DeclareType(new TableArrayType(et));
                break;
            }
            case TypeKind.TableMap:
            {
                var t = (MapTypeRef) tr;
                var kt = ResolveTypeRef(tt, t.KeyType);
                var vt = ResolveTypeRef(tt, t.ValueType);
                result = tt.DeclareType(new TableMapType(kt, vt));
                break;
            }
            case TypeKind.Tuple:
            {
                var t = (TupleTypeRef) tr;
                result = tt.DeclareType(new TupleType(t.ElementTypes.Select(et => new TupleType.Field(ResolveTypeRef(tt, et))).ToList()));
                break;
            }
            case TypeKind.Union:
            {
                var t = (UnionTypeRef) tr;
                result = tt.DeclareType(new UnionType(t.Types.Select(ut => ResolveTypeRef(tt, ut)).ToList()));
                break;
            }
            case TypeKind.Variadic:
            {
                var t = (VariadicTypeRef) tr;
                result = tt.DeclareType(new VariadicType(ResolveTypeRef(tt, t.ElementType)));
                break;
            }
            case TypeKind.Struct:
            {
                var t = (StructTypeRef) tr;
                result = tt.DeclareType(new StructType(t.Fields.Select(f => new StructType.Field(f.Name, ResolveTypeRef(tt, f.Type), f.IsMeta)).ToList()));
                break;
            }
            case TypeKind.Function:
            {
                var t = (FunctionTypeRef) tr;
                var paramTypes = t.ParamTypes.Select(pt => ResolveTypeRef(tt, pt)).ToList();
                var returnType = ResolveTypeRef(tt, t.ReturnType);
                result = tt.DeclareType(new FunctionType(paramTypes, returnType));
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown type kind: {tr.Kind}");
        }

        tr.ResolvedType = result.ID;
        return result;
    }
}