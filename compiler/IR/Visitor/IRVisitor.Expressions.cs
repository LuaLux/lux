using Lux.Diagnostics;

namespace Lux.IR;

internal partial class IRVisitor
{
    #region Literals

    public override Node VisitNilLiteral(LuxParser.NilLiteralContext context)
        => new NilLiteralExpr(NewNodeID, SpanFromCtx(context));

    public override Node VisitTrueLiteral(LuxParser.TrueLiteralContext context)
        => new BoolLiteralExpr(NewNodeID, SpanFromCtx(context), true);

    public override Node VisitFalseLiteral(LuxParser.FalseLiteralContext context)
        => new BoolLiteralExpr(NewNodeID, SpanFromCtx(context), false);

    public override Node VisitNumberLiteral(LuxParser.NumberLiteralContext context)
        => Visit(context.number());

    public override Node VisitStringLiteral(LuxParser.StringLiteralContext context)
        => Visit(context.str());

    public override Node VisitVarargExpr(LuxParser.VarargExprContext context)
        => new VarargExpr(NewNodeID, SpanFromCtx(context));

    #endregion

    #region Number Literals

    public override Node VisitIntLit(LuxParser.IntLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Int);

    public override Node VisitHexLit(LuxParser.HexLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Hex);

    public override Node VisitFloatLit(LuxParser.FloatLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Float);

    public override Node VisitHexFloatLit(LuxParser.HexFloatLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.HexFloat);

    #endregion

    #region String Literals

    public override Node VisitDoubleQuotedStr(LuxParser.DoubleQuotedStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripQuotes(context.GetText()));

    public override Node VisitSingleQuotedStr(LuxParser.SingleQuotedStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripQuotes(context.GetText()));

    public override Node VisitLongStr(LuxParser.LongStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripLongBrackets(context.GetText()));

    public override Node VisitInterpolatedStr(LuxParser.InterpolatedStrContext context)
    {
        var span = SpanFromCtx(context);
        var raw = context.GetText();
        if (raw.Length >= 2 && raw[0] == '`' && raw[^1] == '`')
            raw = raw[1..^1];

        var parts = new List<InterpStringPart>();
        var textBuf = new System.Text.StringBuilder();
        var i = 0;

        while (i < raw.Length)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var esc = raw[i + 1];
                textBuf.Append(esc switch
                {
                    '`' => '`',
                    '{' => '{',
                    '}' => '}',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    _ => esc
                });
                i += 2;
                continue;
            }

            if (raw[i] == '{')
            {
                var depth = 1;
                var start = i + 1;
                var j = start;
                while (j < raw.Length && depth > 0)
                {
                    if (raw[j] == '{') depth++;
                    else if (raw[j] == '}') depth--;
                    if (depth > 0) j++;
                }

                if (depth != 0)
                {
                    textBuf.Append(raw[i]);
                    i++;
                    continue;
                }

                if (textBuf.Length > 0)
                {
                    parts.Add(new InterpTextPart(span, textBuf.ToString()));
                    textBuf.Clear();
                }

                var exprSource = raw[start..j];
                var parsed = ParseInterpolationExpr(exprSource, span);
                if (parsed != null)
                    parts.Add(new InterpExprPart(span, parsed));

                i = j + 1;
                continue;
            }

            textBuf.Append(raw[i]);
            i++;
        }

        if (textBuf.Length > 0)
            parts.Add(new InterpTextPart(span, textBuf.ToString()));

        return new InterpolatedStringExpr(NewNodeID, span, parts);
    }

    private Expr? ParseInterpolationExpr(string exprSource, TextSpan span)
    {
        try
        {
            var input = new Antlr4.Runtime.AntlrInputStream(exprSource);
            var lexer = new LuxLexer(input);
            lexer.RemoveErrorListeners();
            var tokens = new Antlr4.Runtime.CommonTokenStream(lexer);
            var parser = new LuxParser(tokens);
            parser.RemoveErrorListeners();
            var tree = parser.expr();
            if (parser.NumberOfSyntaxErrors > 0) return null;
            var subVisitor = new IRVisitor(filename, nodeAlloc, diag);
            return subVisitor.Visit(tree) as Expr;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Non-Nil Assert

    public override Node VisitNonNilAssertExpr(LuxParser.NonNilAssertExprContext context)
        => new NonNilAssertExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()));

    public override Node VisitPreIncExpr(LuxParser.PreIncExprContext context)
        => new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()), isPre: true, isIncrement: true);

    public override Node VisitPreDecExpr(LuxParser.PreDecExprContext context)
        => new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()), isPre: true, isIncrement: false);

    public override Node VisitPostIncExpr(LuxParser.PostIncExprContext context)
        => new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()), isPre: false, isIncrement: true);

    public override Node VisitPostDecExpr(LuxParser.PostDecExprContext context)
        => new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()), isPre: false, isIncrement: false);

    public override Node VisitTypeCheckExpr(LuxParser.TypeCheckExprContext context)
        => new TypeCheckExpr(NewNodeID, SpanFromCtx(context),
            (Expr)Visit(context.expr()), (TypeRef)Visit(context.typeExpr()));

    public override Node VisitTypeCastExpr(LuxParser.TypeCastExprContext context)
        => new TypeCastExpr(NewNodeID, SpanFromCtx(context),
            (Expr)Visit(context.expr()), (TypeRef)Visit(context.typeExpr()));

    public override Node VisitTypeOfExpr(LuxParser.TypeOfExprContext context)
        => new TypeOfExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()));

    public override Node VisitInstanceOfExpr(LuxParser.InstanceOfExprContext context)
    {
        var typeRef = (TypeRef)Visit(context.typeAtom());
        NameRef className;
        switch (typeRef)
        {
            case NamedTypeRef nt:
                className = nt.Name;
                break;
            case GenericTypeRef gt:
                className = gt.Name;
                break;
            default:
                diag.Report(SpanFromCtx(context.typeAtom()), DiagnosticCode.ErrInstanceOfNonClass, typeRef.GetType().Name);
                className = NameRefFromText("<error>", SpanFromCtx(context.typeAtom()));
                break;
        }
        var inner = (Expr)Visit(context.expr());
        var expr = new InstanceOfExpr(NewNodeID, SpanFromCtx(context), inner, className);
        expr.TargetType = typeRef;
        return expr;
    }

    #endregion

    #region Binary Expressions

    public override Node VisitLogicalOrExpr(LuxParser.LogicalOrExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Or, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitAltLogicalOrExpr(LuxParser.AltLogicalOrExprContext context)
    {
        CheckAltBooleanOperator(SpanFromCtx(context), "||", "or");
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Or, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitNilCoalesceExpr(LuxParser.NilCoalesceExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.NilCoalesce, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitLogicalAndExpr(LuxParser.LogicalAndExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.And, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitAltLogicalAndExpr(LuxParser.AltLogicalAndExprContext context)
    {
        CheckAltBooleanOperator(SpanFromCtx(context), "&&", "and");
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.And, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitComparisonExpr(LuxParser.ComparisonExprContext context)
    {
        var compareOp = context.compareOp();
        if (compareOp is LuxParser.AltNeqOpContext)
            CheckAltBooleanOperator(SpanFromCtx(compareOp), "!=", "~=");
        var op = compareOp switch
        {
            LuxParser.LtOpContext => BinaryOp.Lt,
            LuxParser.GtOpContext => BinaryOp.Gt,
            LuxParser.LteOpContext => BinaryOp.Lte,
            LuxParser.GteOpContext => BinaryOp.Gte,
            LuxParser.NeqOpContext => BinaryOp.Neq,
            LuxParser.AltNeqOpContext => BinaryOp.Neq,
            LuxParser.EqOpContext => BinaryOp.Eq,
            _ => throw new InvalidOperationException("Unknown compare op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitBitwiseOrExpr(LuxParser.BitwiseOrExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseOr, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitwiseXorExpr(LuxParser.BitwiseXorExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseXor, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitwiseAndExpr(LuxParser.BitwiseAndExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseAnd, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitShiftExpr(LuxParser.BitShiftExprContext context)
    {
        var op = context.shiftOp() switch
        {
            LuxParser.LshiftOpContext => BinaryOp.LShift,
            LuxParser.RshiftOpContext => BinaryOp.RShift,
            _ => throw new InvalidOperationException("Unknown shift op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitConcatExpr(LuxParser.ConcatExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Concat, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitAdditiveExpr(LuxParser.AdditiveExprContext context)
    {
        var op = context.additiveOp() switch
        {
            LuxParser.AddOpContext => BinaryOp.Add,
            LuxParser.SubOpContext => BinaryOp.Sub,
            _ => throw new InvalidOperationException("Unknown additive op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitMultiplicativeExpr(LuxParser.MultiplicativeExprContext context)
    {
        var op = context.multiplicativeOp() switch
        {
            LuxParser.MulOpContext => BinaryOp.Mul,
            LuxParser.DivOpContext => BinaryOp.Div,
            LuxParser.FloorDivOpContext => BinaryOp.FloorDiv,
            LuxParser.ModOpContext => BinaryOp.Mod,
            _ => throw new InvalidOperationException("Unknown multiplicative op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitPowerExpr(LuxParser.PowerExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Pow, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    #endregion

    #region Unary Expressions

    public override Node VisitUnaryExpr(LuxParser.UnaryExprContext context)
    {
        var op = context.unaryOp() switch
        {
            LuxParser.LogicalNotOpContext => UnaryOp.LogicalNot,
            LuxParser.LengthOpContext => UnaryOp.Length,
            LuxParser.NegateOpContext => UnaryOp.Negate,
            LuxParser.BitwiseNotOpContext => UnaryOp.BitwiseNot,
            _ => throw new InvalidOperationException("Unknown unary op")
        };
        return new UnaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr()));
    }

    public override Node VisitAltLogicalNotExpr(LuxParser.AltLogicalNotExprContext context)
    {
        CheckAltBooleanOperator(SpanFromCtx(context), "!", "not");
        return new UnaryExpr(NewNodeID, SpanFromCtx(context), UnaryOp.LogicalNot, (Expr)Visit(context.expr()));
    }

    private void CheckAltBooleanOperator(TextSpan span, string altForm, string luaForm)
    {
        if (!_config.Code.AltBooleanOperators)
            diag.Report(span, DiagnosticCode.ErrAltBooleanOperatorsDisabled, altForm, luaForm);
    }

    #endregion

    #region Prefix Expressions

    public override Node VisitPrefixExpr(LuxParser.PrefixExprContext context)
        => Visit(context.prefixExp());

    public override Node VisitPrefixExp(LuxParser.PrefixExpContext context)
        => BuildSuffixChain(context.varOrExp(), context.suffix());

    public override Node VisitNameVarOrExp(LuxParser.NameVarOrExpContext context)
        => new NameExpr(NewNodeID, SpanFromCtx(context), new NameRef(context.NAME().GetText(), SpanFromTerm(context.NAME())));

    public override Node VisitParenVarOrExp(LuxParser.ParenVarOrExpContext context)
        => new ParenExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()));

    #endregion

    #region Variable references (assignment targets)

    public override Node VisitNameVar(LuxParser.NameVarContext context)
        => new NameExpr(NewNodeID, SpanFromCtx(context), new NameRef(context.NAME().GetText(), SpanFromTerm(context.NAME())));

    public override Node VisitFieldVar(LuxParser.FieldVarContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new DotAccessExpr(NewNodeID, SpanFromCtx(context), obj, NameRefFromTerm(context.NAME()));
    }

    public override Node VisitIndexVar(LuxParser.IndexVarContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new IndexAccessExpr(NewNodeID, SpanFromCtx(context), obj, (Expr)Visit(context.expr()));
    }

    #endregion

    #region Function Calls

    public override Node VisitDirectCall(LuxParser.DirectCallContext context)
    {
        var callee = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new FunctionCallExpr(NewNodeID, SpanFromCtx(context), callee, VisitArgsContent(context.args()));
    }

    public override Node VisitMethodCall(LuxParser.MethodCallContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new MethodCallExpr(NewNodeID, SpanFromCtx(context), obj, NameRefFromTerm(context.NAME()), VisitArgsContent(context.args()));
    }

    #endregion

    #region Function Definitions (anonymous)

    public override Node VisitFunctionDefExpr(LuxParser.FunctionDefExprContext context)
        => Visit(context.functionDef());

    public override Node VisitFunctionDef(LuxParser.FunctionDefContext context)
    {
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        var isAsync = context.ASYNC() != null;
        var typeParams = VisitTypeParamListContent(context.funcBody().typeParamList());
        var expr = new FunctionDefExpr(NewNodeID, SpanFromCtx(context), parameters, returnType, body, ret, isAsync);
        expr.TypeParams = typeParams;
        return expr;
    }

    public override Node VisitAwaitExpr(LuxParser.AwaitExprContext context)
    {
        var inner = (Expr)Visit(context.expr());
        return new AwaitExpr(NewNodeID, SpanFromCtx(context), inner);
    }

    public override Node VisitNewExpr(LuxParser.NewExprContext context)
    {
        var className = NameRefFromTerm(context.NAME());
        var args = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        return new NewExpr(NewNodeID, SpanFromCtx(context), className, args);
    }

    public override Node VisitSuperCallExpr(LuxParser.SuperCallExprContext context)
    {
        var args = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        return new SuperCallExpr(NewNodeID, SpanFromCtx(context), args);
    }

    #endregion

    #region Tables

    public override Node VisitTableConstructorExpr(LuxParser.TableConstructorExprContext context)
        => Visit(context.tableConstructor());

    public override Node VisitTableConstructor(LuxParser.TableConstructorContext context)
    {
        var fields = new List<TableField>();
        if (context.fieldList() != null)
        {
            foreach (var field in context.fieldList().field())
                fields.Add(VisitTableFieldContent(field));
        }

        return new TableConstructorExpr(NewNodeID, SpanFromCtx(context), fields);
    }

    private TableField VisitTableFieldContent(LuxParser.FieldContext ctx)
    {
        return ctx switch
        {
            LuxParser.BracketFieldContext bf => new TableField(
                TableFieldKind.Bracket, (Expr)Visit(bf.expr(0)), null, (Expr)Visit(bf.expr(1)), SpanFromCtx(bf)),
            LuxParser.NameFieldContext nf => new TableField(
                TableFieldKind.Named, null, NameRefFromTerm(nf.NAME()), (Expr)Visit(nf.expr()), SpanFromCtx(nf)),
            LuxParser.FunctionFieldContext ff => new TableField(
                TableFieldKind.Named, null, NameRefFromTerm(ff.NAME()), BuildFieldFunction(ff), SpanFromCtx(ff)),
            LuxParser.ValueFieldContext vf => new TableField(
                TableFieldKind.Positional, null, null, (Expr)Visit(vf.expr()), SpanFromCtx(vf)),
            _ => throw new InvalidOperationException($"Unknown field type: {ctx.GetType().Name}")
        };
    }

    /// <summary>
    /// Desugars the named-function field shorthand <c>{ function foo(...) ... end }</c> into the
    /// anonymous function that <c>{ foo = function(...) ... end }</c> would have produced. The name
    /// becomes the field key, so nothing downstream has to know the shorthand existed.
    /// </summary>
    private FunctionDefExpr BuildFieldFunction(LuxParser.FunctionFieldContext ctx)
    {
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(ctx.funcBody());
        var expr = new FunctionDefExpr(NewNodeID, SpanFromCtx(ctx), parameters, returnType, body, ret, ctx.ASYNC() != null)
        {
            TypeParams = VisitTypeParamListContent(ctx.funcBody().typeParamList())
        };
        return expr;
    }

    #endregion
}
