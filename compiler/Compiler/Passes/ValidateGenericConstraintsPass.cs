using Lux.Diagnostics;
using Lux.IR;

namespace Lux.Compiler.Passes;

/// <summary>
/// Walks every <see cref="GenericTypeRef"/> in the IR and verifies that each
/// supplied type argument satisfies the corresponding type parameter's
/// <c>extends</c> / <c>implements</c> bounds. Runs after <see cref="InferTypesPass"/>
/// so the class hierarchy (BaseClass, Interfaces) is fully wired up before the
/// subtype check fires.
/// </summary>
public sealed class ValidateGenericConstraintsPass() : Pass(PassName, PassScope.PerBuild, noErrors: false, InferTypesPass.PassName)
{
    public const string PassName = "ValidateGenericConstraints";

    public override bool Run(PassContext context)
    {
        foreach (var pkg in context.Pkgs)
        {
            foreach (var file in pkg.Files)
            {
                foreach (var stmt in file.Hir.Body)
                    VisitStmt(context, pkg, stmt);
                if (file.Hir.Return != null)
                    VisitStmt(context, pkg, file.Hir.Return);
            }
        }
        return true;
    }

    private void VisitStmt(PassContext ctx, PackageContext pkg, Stmt stmt)
    {
        switch (stmt)
        {
            case Decl decl: VisitDecl(ctx, pkg, decl); break;
            case AssignStmt a:
                foreach (var t in a.Targets) VisitExpr(ctx, pkg, t);
                foreach (var v in a.Values) VisitExpr(ctx, pkg, v);
                break;
            case ExprStmt es: VisitExpr(ctx, pkg, es.Expression); break;
            case DoBlockStmt db: foreach (var s in db.Body) VisitStmt(ctx, pkg, s); break;
            case WhileStmt w: VisitExpr(ctx, pkg, w.Condition); foreach (var s in w.Body) VisitStmt(ctx, pkg, s); break;
            case RepeatStmt r: foreach (var s in r.Body) VisitStmt(ctx, pkg, s); VisitExpr(ctx, pkg, r.Condition); break;
            case IfStmt ifs:
                VisitExpr(ctx, pkg, ifs.Condition);
                foreach (var s in ifs.Body) VisitStmt(ctx, pkg, s);
                foreach (var ei in ifs.ElseIfs) { VisitExpr(ctx, pkg, ei.Condition); foreach (var s in ei.Body) VisitStmt(ctx, pkg, s); }
                if (ifs.ElseBody != null) foreach (var s in ifs.ElseBody) VisitStmt(ctx, pkg, s);
                break;
            case NumericForStmt nf:
                VisitExpr(ctx, pkg, nf.Start); VisitExpr(ctx, pkg, nf.Limit);
                if (nf.Step != null) VisitExpr(ctx, pkg, nf.Step);
                foreach (var s in nf.Body) VisitStmt(ctx, pkg, s);
                break;
            case GenericForStmt gf:
                foreach (var v in gf.Iterators) VisitExpr(ctx, pkg, v);
                foreach (var s in gf.Body) VisitStmt(ctx, pkg, s);
                break;
            case ReturnStmt rs: foreach (var v in rs.Values) VisitExpr(ctx, pkg, v); break;
            case ExportStmt es: VisitDecl(ctx, pkg, es.Declaration); break;
            case GuardStmt gs: VisitExpr(ctx, pkg, gs.Condition); if (gs.ElseExpr != null) VisitExpr(ctx, pkg, gs.ElseExpr); break;
            case DeferStmt ds:
                if (ds.Call != null) VisitExpr(ctx, pkg, ds.Call);
                if (ds.Block != null) foreach (var s in ds.Block) VisitStmt(ctx, pkg, s);
                break;
            case MatchStmt ms:
                VisitExpr(ctx, pkg, ms.Scrutinee);
                foreach (var arm in ms.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) VisitExpr(ctx, pkg, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) VisitTypeRef(ctx, pkg, arm.Pattern.TypeRef);
                    if (arm.Guard != null) VisitExpr(ctx, pkg, arm.Guard);
                    foreach (var s in arm.Body) VisitStmt(ctx, pkg, s);
                }
                break;
        }
    }

    private void VisitDecl(PassContext ctx, PackageContext pkg, Decl decl)
    {
        switch (decl)
        {
            case LocalDecl ld:
                foreach (var v in ld.Variables)
                    if (v.TypeAnnotation != null) VisitTypeRef(ctx, pkg, v.TypeAnnotation);
                foreach (var v in ld.Values) VisitExpr(ctx, pkg, v);
                break;
            case FunctionDecl fd:
                foreach (var p in fd.Parameters)
                {
                    if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (p.DefaultValue != null) VisitExpr(ctx, pkg, p.DefaultValue);
                }
                if (fd.ReturnType != null) VisitTypeRef(ctx, pkg, fd.ReturnType);
                foreach (var s in fd.Body) VisitStmt(ctx, pkg, s);
                if (fd.ReturnStmt != null) VisitStmt(ctx, pkg, fd.ReturnStmt);
                break;
            case LocalFunctionDecl lfd:
                foreach (var p in lfd.Parameters)
                {
                    if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (p.DefaultValue != null) VisitExpr(ctx, pkg, p.DefaultValue);
                }
                if (lfd.ReturnType != null) VisitTypeRef(ctx, pkg, lfd.ReturnType);
                foreach (var s in lfd.Body) VisitStmt(ctx, pkg, s);
                if (lfd.ReturnStmt != null) VisitStmt(ctx, pkg, lfd.ReturnStmt);
                break;
            case DeclareFunctionDecl dfd:
                foreach (var p in dfd.Parameters)
                    if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                if (dfd.ReturnType != null) VisitTypeRef(ctx, pkg, dfd.ReturnType);
                break;
            case DeclareVariableDecl dvd: VisitTypeRef(ctx, pkg, dvd.TypeAnnotation); break;
            case DeclareModuleDecl dmd: foreach (var m in dmd.Members) VisitDecl(ctx, pkg, m); break;
            case ClassDecl cd:
                foreach (var f in cd.Fields)
                {
                    if (f.TypeAnnotation != null) VisitTypeRef(ctx, pkg, f.TypeAnnotation);
                    if (f.DefaultValue != null) VisitExpr(ctx, pkg, f.DefaultValue);
                }
                if (cd.Constructor != null)
                {
                    foreach (var p in cd.Constructor.Parameters)
                        if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    foreach (var s in cd.Constructor.Body) VisitStmt(ctx, pkg, s);
                }
                foreach (var m in cd.Methods)
                {
                    foreach (var p in m.Parameters)
                        if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (m.ReturnType != null) VisitTypeRef(ctx, pkg, m.ReturnType);
                    foreach (var s in m.Body) VisitStmt(ctx, pkg, s);
                }
                foreach (var a in cd.Accessors)
                {
                    foreach (var p in a.Parameters)
                        if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (a.ReturnType != null) VisitTypeRef(ctx, pkg, a.ReturnType);
                    foreach (var s in a.Body) VisitStmt(ctx, pkg, s);
                }
                break;
            case InterfaceDecl iface:
                foreach (var f in iface.Fields) VisitTypeRef(ctx, pkg, f.TypeAnnotation);
                foreach (var m in iface.Methods)
                {
                    foreach (var p in m.Parameters)
                        if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (m.ReturnType != null) VisitTypeRef(ctx, pkg, m.ReturnType);
                }
                break;
        }
    }

    private void VisitExpr(PassContext ctx, PackageContext pkg, Expr expr)
    {
        switch (expr)
        {
            case TypeCastExpr tc:
                VisitExpr(ctx, pkg, tc.Inner);
                VisitTypeRef(ctx, pkg, tc.TargetType);
                break;
            case TypeCheckExpr chk:
                VisitExpr(ctx, pkg, chk.Inner);
                VisitTypeRef(ctx, pkg, chk.TargetType);
                break;
            case BinaryExpr b: VisitExpr(ctx, pkg, b.Left); VisitExpr(ctx, pkg, b.Right); break;
            case UnaryExpr u: VisitExpr(ctx, pkg, u.Operand); break;
            case FunctionCallExpr fc:
                VisitExpr(ctx, pkg, fc.Callee);
                foreach (var a in fc.Arguments) VisitExpr(ctx, pkg, a);
                break;
            case MethodCallExpr mc:
                VisitExpr(ctx, pkg, mc.Object);
                foreach (var a in mc.Arguments) VisitExpr(ctx, pkg, a);
                break;
            case IndexAccessExpr idx: VisitExpr(ctx, pkg, idx.Object); VisitExpr(ctx, pkg, idx.Index); break;
            case DotAccessExpr fa: VisitExpr(ctx, pkg, fa.Object); break;
            case ParenExpr pe: VisitExpr(ctx, pkg, pe.Inner); break;
            case NonNilAssertExpr nna: VisitExpr(ctx, pkg, nna.Inner); break;
            case IncDecExpr inc: VisitExpr(ctx, pkg, inc.Target); break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    if (f.Key != null) VisitExpr(ctx, pkg, f.Key);
                    VisitExpr(ctx, pkg, f.Value);
                }
                break;
            case FunctionDefExpr fd:
                foreach (var p in fd.Parameters)
                {
                    if (p.TypeAnnotation != null) VisitTypeRef(ctx, pkg, p.TypeAnnotation);
                    if (p.DefaultValue != null) VisitExpr(ctx, pkg, p.DefaultValue);
                }
                if (fd.ReturnType != null) VisitTypeRef(ctx, pkg, fd.ReturnType);
                foreach (var s in fd.Body) VisitStmt(ctx, pkg, s);
                if (fd.ReturnStmt != null) VisitStmt(ctx, pkg, fd.ReturnStmt);
                break;
            case AwaitExpr aw: VisitExpr(ctx, pkg, aw.Expression); break;
            case NewExpr ne:
                foreach (var a in ne.Arguments) VisitExpr(ctx, pkg, a);
                break;
            case SuperCallExpr sc: foreach (var a in sc.Arguments) VisitExpr(ctx, pkg, a); break;
            case MatchExpr me:
                VisitExpr(ctx, pkg, me.Scrutinee);
                foreach (var arm in me.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) VisitExpr(ctx, pkg, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) VisitTypeRef(ctx, pkg, arm.Pattern.TypeRef);
                    if (arm.Guard != null) VisitExpr(ctx, pkg, arm.Guard);
                    VisitExpr(ctx, pkg, arm.Value);
                }
                break;
        }
    }

    private void VisitTypeRef(PassContext ctx, PackageContext pkg, TypeRef tr)
    {
        switch (tr)
        {
            case GenericTypeRef gtr:
                ValidateGenericInstantiation(ctx, pkg, gtr);
                foreach (var arg in gtr.Arguments)
                {
                    if (arg is ConcreteTypeArgRef cta) VisitTypeRef(ctx, pkg, cta.Type);
                    else if (arg is WildcardTypeArgRef wta && wta.Bound != null) VisitTypeRef(ctx, pkg, wta.Bound);
                }
                break;
            case NullableTypeRef nt: VisitTypeRef(ctx, pkg, nt.InnerType); break;
            case UnionTypeRef ut: foreach (var t in ut.Types) VisitTypeRef(ctx, pkg, t); break;
            case ArrayTypeRef at: VisitTypeRef(ctx, pkg, at.ElementType); break;
            case MapTypeRef mt: VisitTypeRef(ctx, pkg, mt.KeyType); VisitTypeRef(ctx, pkg, mt.ValueType); break;
            case TupleTypeRef tt: foreach (var t in tt.ElementTypes) VisitTypeRef(ctx, pkg, t); break;
            case StructTypeRef st: foreach (var f in st.Fields) VisitTypeRef(ctx, pkg, f.Type); break;
            case FunctionTypeRef ft:
                foreach (var p in ft.ParamTypes) VisitTypeRef(ctx, pkg, p);
                VisitTypeRef(ctx, pkg, ft.ReturnType);
                break;
        }
    }

    private void ValidateGenericInstantiation(PassContext ctx, PackageContext pkg, GenericTypeRef gtr)
    {
        if (!pkg.Types.GetByID(gtr.ResolvedType, out var headType)) return;

        List<TypeParameterType> paramDefs;
        switch (headType)
        {
            case ClassType ct: paramDefs = ct.TypeParams; break;
            case InterfaceType it: paramDefs = it.TypeParams; break;
            default: return;
        }

        var pairCount = Math.Min(gtr.Arguments.Count, paramDefs.Count);
        for (var i = 0; i < pairCount; i++)
        {
            var paramDef = paramDefs[i];
            if (gtr.Arguments[i] is not ConcreteTypeArgRef concrete) continue;
            var argTypeId = concrete.Type.ResolvedType;
            if (argTypeId == TypID.Invalid) continue;
            if (!pkg.Types.GetByID(argTypeId, out var argType)) continue;

            if (paramDef.ExtendsBound is { } ext && !IsSubtype(pkg, argType, ext))
            {
                var argName = TypeName(pkg, argTypeId);
                var boundName = TypeName(pkg, ext);
                ctx.Diag.Report(concrete.Type.Span, DiagnosticCode.ErrTypeParamBoundViolation,
                    argName, boundName, paramDef.Name);
            }

            foreach (var ib in paramDef.ImplementsBounds)
            {
                if (!IsSubtype(pkg, argType, ib))
                {
                    var argName = TypeName(pkg, argTypeId);
                    var boundName = TypeName(pkg, ib);
                    ctx.Diag.Report(concrete.Type.Span, DiagnosticCode.ErrTypeParamBoundViolation,
                        argName, boundName, paramDef.Name);
                }
            }
        }
    }

    /// <summary>
    /// Checks whether <paramref name="arg"/> satisfies the constraint represented
    /// by the type with id <paramref name="boundId"/>. Treats <c>any</c> as a
    /// universal escape hatch on either side, walks the BaseClass chain for
    /// class-extends, and walks Interfaces / BaseInterfaces for interface
    /// implementation. Type parameters are accepted if their own bound is
    /// compatible with the constraint.
    /// </summary>
    private static bool IsSubtype(PackageContext pkg, IR.Type arg, TypID boundId)
    {
        if (arg.ID == boundId) return true;
        if (!pkg.Types.GetByID(boundId, out var bound)) return true;
        if (arg.Kind == TypeKind.PrimitiveAny || bound.Kind == TypeKind.PrimitiveAny) return true;

        if (arg is TypeParameterType tp)
        {
            if (tp.ExtendsBound is { } eb && pkg.Types.GetByID(eb, out var ebType))
                return IsSubtype(pkg, ebType, boundId);
            return false;
        }

        switch (bound)
        {
            case ClassType boundClass:
                if (arg is ClassType ac)
                {
                    var cur = ac.BaseClass;
                    while (cur != null)
                    {
                        if (cur.ID == boundClass.ID) return true;
                        cur = cur.BaseClass;
                    }
                }
                return false;
            case InterfaceType boundIface:
                if (arg is ClassType acIface && ClassImplements(acIface, boundIface)) return true;
                if (arg is InterfaceType ai && InterfaceExtends(ai, boundIface)) return true;
                return false;
            default:
                return arg.Kind == bound.Kind;
        }
    }

    private static bool ClassImplements(ClassType cls, InterfaceType target)
    {
        var visited = new HashSet<int>();
        var current = cls;
        while (current != null)
        {
            foreach (var iface in current.Interfaces)
                if (InterfaceExtends(iface, target, visited)) return true;
            current = current.BaseClass;
        }
        return false;
    }

    private static bool InterfaceExtends(InterfaceType iface, InterfaceType target, HashSet<int>? visited = null)
    {
        if (iface.ID == target.ID) return true;
        visited ??= new HashSet<int>();
        if (!visited.Add((int)iface.ID.Value)) return false;
        foreach (var b in iface.BaseInterfaces)
            if (InterfaceExtends(b, target, visited)) return true;
        return false;
    }

    private static string TypeName(PackageContext pkg, TypID id)
    {
        if (!pkg.Types.GetByID(id, out var t)) return "<unknown>";
        return t switch
        {
            ClassType c => c.Name,
            InterfaceType i => i.Name,
            EnumType e => e.Name,
            TypeParameterType p => p.Name,
            _ => t.Kind switch
            {
                TypeKind.PrimitiveString => "string",
                TypeKind.PrimitiveNumber => "number",
                TypeKind.PrimitiveBool => "boolean",
                TypeKind.PrimitiveNil => "nil",
                TypeKind.PrimitiveAny => "any",
                TypeKind.PrimitiveNever => "never",
                _ => t.Kind.ToString()
            }
        };
    }
}
