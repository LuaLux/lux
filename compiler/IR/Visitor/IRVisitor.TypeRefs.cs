using Lux.Diagnostics;

namespace Lux.IR;

internal partial class IRVisitor
{
    private static readonly Dictionary<string, TypeKind> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "string", TypeKind.PrimitiveString },
        { "number", TypeKind.PrimitiveNumber },
        { "boolean", TypeKind.PrimitiveBool },
        { "any", TypeKind.PrimitiveAny },
        { "thread", TypeKind.PrimitiveThread },
        { "userdata", TypeKind.PrimitiveUserdata },
        { "never", TypeKind.PrimitiveNever }
    };

    public override Node VisitBareFunctionType(LuxParser.BareFunctionTypeContext context)
        => new PrimitiveTypeRef(NewNodeID, SpanFromCtx(context), TypeKind.PrimitiveFunction);
    
    public override Node VisitUnionType(LuxParser.UnionTypeContext context)
    {
        var types = context.typeSingle().Select(t => (TypeRef)Visit(t)).ToList();
        if (types.Count == 1) return types[0];
        return new UnionTypeRef(NewNodeID, SpanFromCtx(context), types);
    }

    public override Node VisitPostfixType(LuxParser.PostfixTypeContext context)
    {
        var result = (TypeRef)Visit(context.typeAtom());
        foreach (var suffix in context.typeSuffix())
        {
            result = suffix switch
            {
                LuxParser.ArraySuffixContext => new ArrayTypeRef(NewNodeID, SpanFromCtx(suffix), result),
                LuxParser.NullableSuffixContext => new NullableTypeRef(NewNodeID, SpanFromCtx(suffix), result),
                _ => throw new InvalidOperationException($"Unknown type suffix: {suffix.GetType().Name}")
            };
        }

        return result;
    }

    public override Node VisitNilType(LuxParser.NilTypeContext context)
        => new PrimitiveTypeRef(NewNodeID, SpanFromCtx(context), TypeKind.PrimitiveNil);

    public override Node VisitNamedType(LuxParser.NamedTypeContext context)
    {
        var nameText = context.NAME().GetText();
        var typeArgList = context.typeArgList();

        if (typeArgList != null)
        {
            if (PrimitiveTypes.TryGetValue(nameText, out var value))
            {
                diag.Report(SpanFromCtx(context), DiagnosticCode.ErrGenericOnPrimitive, nameText);
                return new PrimitiveTypeRef(NewNodeID, SpanFromCtx(context), value);
            }

            var args = VisitTypeArgListContent(typeArgList);
            return new GenericTypeRef(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), args);
        }

        if (PrimitiveTypes.TryGetValue(nameText, out var kind))
        {
            return new PrimitiveTypeRef(NewNodeID, SpanFromCtx(context), kind);
        }

        // `void` documents "no return value". It is an alias for `nil`: a function
        // that returns nothing yields `nil`, and a `nil`/`void` return is exempt from
        // the missing-return check. Modelling true zero-value `none` semantics (distinct
        // from `nil`) is tracked with the return-arity work (variadic returns).
        if (string.Equals(nameText, "void", StringComparison.OrdinalIgnoreCase))
        {
            return new PrimitiveTypeRef(NewNodeID, SpanFromCtx(context), TypeKind.PrimitiveNil);
        }

        return new NamedTypeRef(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()));
    }

    public override Node VisitVariadicType(LuxParser.VariadicTypeContext context)
        => new VariadicTypeRef(NewNodeID, SpanFromCtx(context), (TypeRef)Visit(context.typeSingle()));

    public override Node VisitFuncType(LuxParser.FuncTypeContext context)
        => Visit(context.functionType());

    public override Node VisitTableType_(LuxParser.TableType_Context context)
        => Visit(context.tableType());

    public override Node VisitGroupedOrTupleType(LuxParser.GroupedOrTupleTypeContext context)
    {
        var types = context.typeExpr().Select(t => (TypeRef)Visit(t)).ToList();
        if (types.Count == 1) return types[0];
        return new TupleTypeRef(NewNodeID, SpanFromCtx(context), types);
    }

    public override Node VisitFunctionType(LuxParser.FunctionTypeContext context)
    {
        var paramTypes = context.typeList()?.typeExpr().Select(t => (TypeRef)Visit(t)).ToList() ?? [];
        var returnType = (TypeRef)Visit(context.typeExpr());
        return new FunctionTypeRef(NewNodeID, SpanFromCtx(context), paramTypes, returnType);
    }

    public override Node VisitEmptyTableType(LuxParser.EmptyTableTypeContext context)
        => new StructTypeRef(NewNodeID, SpanFromCtx(context), []);

    public override Node VisitMapType(LuxParser.MapTypeContext context)
    {
        var keyType = (TypeRef)Visit(context.typeExpr(0));
        var valueType = (TypeRef)Visit(context.typeExpr(1));
        return new MapTypeRef(NewNodeID, SpanFromCtx(context), keyType, valueType);
    }

    public override Node VisitStructType(LuxParser.StructTypeContext context)
    {
        var fields = context.structField().Select(f => new StructTypeField(
            NameRefFromTerm(f.NAME()),
            (TypeRef)Visit(f.typeExpr()),
            f.META() != null,
            SpanFromCtx(f)
        )).ToList();
        return new StructTypeRef(NewNodeID, SpanFromCtx(context), fields);
    }
}
