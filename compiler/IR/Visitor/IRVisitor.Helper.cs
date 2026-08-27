using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Nebra.Diagnostics;

namespace Nebra.IR;

internal partial class IRVisitor
{
    private NodeID NewNodeID => nodeAlloc.Next();

    private TextSpan SpanFromCtx(ParserRuleContext ctx)
    {
        var start = ctx.Start;
        var stop = ctx.Stop ?? start;
        var startCol = start.Column + 1;
        var endCol = stop.Column + 1 + (stop.StopIndex - stop.StartIndex) + 1;
        return new TextSpan(filename, start.Line, startCol, stop.Line, endCol);
    }

    private TextSpan SpanFromTok(IToken tok) => TextSpan.Of(tok, filename);

    private TextSpan SpanFromTerm(ITerminalNode term) => TextSpan.Of(term, filename);

    /// <summary>
    /// Strips the surrounding quote characters and decodes the LuaCATS-style
    /// escape sequences (<c>\n</c>, <c>\t</c>, <c>\xNN</c>, <c>\u{NNNN}</c>,
    /// decimal escapes, line-continuation). The returned string holds the
    /// actual runtime characters; codegen later re-encodes them when emitting
    /// Lua source.
    /// </summary>
    private static string StripQuotes(string s)
    {
        if (s.Length < 2) return s;
        return DecodeStringEscapes(s[1..^1]);
    }

    private static string DecodeStringEscapes(string raw)
    {
        if (raw.IndexOf('\\') < 0) return raw;
        var sb = new System.Text.StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c != '\\' || i + 1 >= raw.Length) { sb.Append(c); continue; }
            var n = raw[i + 1];
            switch (n)
            {
                case 'a': sb.Append('\a'); i++; break;
                case 'b': sb.Append('\b'); i++; break;
                case 'f': sb.Append('\f'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'v': sb.Append('\v'); i++; break;
                case 'z': i++; while (i + 1 < raw.Length && char.IsWhiteSpace(raw[i + 1])) i++; break;
                case '"': sb.Append('"'); i++; break;
                case '\'': sb.Append('\''); i++; break;
                case '`': sb.Append('`'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case '\n': sb.Append('\n'); i++; break;
                case '\r':
                    sb.Append('\n');
                    i++;
                    if (i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                    break;
                case 'x':
                {
                    var hex = "";
                    var k = i + 2;
                    while (hex.Length < 2 && k < raw.Length && IsHexDigit(raw[k])) { hex += raw[k]; k++; }
                    if (hex.Length > 0) { sb.Append((char)int.Parse(hex, System.Globalization.NumberStyles.HexNumber)); i = k - 1; }
                    else { sb.Append(c); }
                    break;
                }
                case 'u':
                {
                    if (i + 2 < raw.Length && raw[i + 2] == '{')
                    {
                        var end = raw.IndexOf('}', i + 3);
                        if (end > 0)
                        {
                            var hex = raw[(i + 3)..end];
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var cp))
                                sb.Append(char.ConvertFromUtf32(cp));
                            i = end;
                            break;
                        }
                    }
                    sb.Append(c);
                    break;
                }
                default:
                    if (char.IsDigit(n))
                    {
                        var num = "";
                        var k = i + 1;
                        while (num.Length < 3 && k < raw.Length && char.IsDigit(raw[k])) { num += raw[k]; k++; }
                        if (int.TryParse(num, out var dec) && dec < 256) { sb.Append((char)dec); i = k - 1; }
                        else sb.Append(c);
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static string StripLongBrackets(string s)
    {
        var eqCount = 0;
        for (var i = 1; i < s.Length && s[i] == '='; i++) eqCount++;
        var start = eqCount + 2;
        var end = s.Length - eqCount - 2;
        return start < end ? s[start..end] : string.Empty;
    }

    private string ParseStringValue(NebraParser.StrContext? ctx)
    {
        // ctx can be null when ANTLR error-recovery synthesizes a parse tree for
        // malformed input (e.g. `import { a, b }` without the trailing `from "..."`).
        // The syntax error has already been reported by the error listener; we just
        // avoid dereferencing the missing node so we don't crash on top of it.
        if (ctx == null) return string.Empty;
        return ctx switch
        {
            NebraParser.DoubleQuotedStrContext => StripQuotes(ctx.GetText()),
            NebraParser.SingleQuotedStrContext => StripQuotes(ctx.GetText()),
            NebraParser.LongStrContext => StripLongBrackets(ctx.GetText()),
            _ => ctx.GetText()
        };
    }

    private (string, TextSpan) ParseStringValueWithSpan(NebraParser.StrContext? ctx)
    {
        var value = ParseStringValue(ctx);
        var span = ctx != null ? SpanFromCtx(ctx) : TextSpan.Empty;
        return (value, span);
    }

    private NameRef NameRefFromString(NebraParser.StrContext? ctx)
    {
        var (value, span) = ParseStringValueWithSpan(ctx);
        return new NameRef(value, span);
    }
    
    private TypeRef? VisitTypeAnnotationOpt(NebraParser.TypeAnnotationContext? ctx)
    {
        if (ctx == null) return null;
        return (TypeRef)Visit(ctx.typeExpr());
    }

    private (List<Stmt> body, ReturnStmt? ret) VisitBlockContent(NebraParser.BlockContext ctx)
    {
        var stmts = new List<Stmt>();
        if (ctx == null) return (stmts, null);
        var stmtCtxs = ctx.stmt();
        if (stmtCtxs == null) return (stmts, null);
        foreach (var stmt in stmtCtxs)
        {
            if (stmt == null) continue;
            var node = Visit(stmt);
            if (node is Stmt s) stmts.Add(s);
        }

        ReturnStmt? ret = null;
        if (ctx.returnStat() != null)
            ret = (ReturnStmt)Visit(ctx.returnStat());

        return (stmts, ret);
    }

    private (List<Parameter> parameters, TypeRef? returnType, List<Stmt> body, ReturnStmt? ret) VisitFuncBodyContent(
        NebraParser.FuncBodyContext ctx)
    {
        var parameters = VisitParamListContent(ctx.paramList());
        var returnType = VisitFuncReturnOpt(ctx.funcReturn());
        var (body, ret) = VisitBlockContent(ctx.block());
        return (parameters, returnType, body, ret);
    }

    private (List<Parameter> parameters, TypeRef? returnType) VisitFuncSignatureContent(
        NebraParser.FuncSignatureContext ctx)
    {
        var parameters = VisitParamListContent(ctx.paramList());
        var returnType = VisitFuncReturnOpt(ctx.funcReturn());
        return (parameters, returnType);
    }

    /// <summary>
    /// A function return is either a plain type or a type predicate <c>param is Type</c>
    /// (produces a <see cref="TypePredicateRef"/>).
    /// </summary>
    private TypeRef? VisitFuncReturnOpt(NebraParser.FuncReturnContext? ctx)
    {
        switch (ctx)
        {
            case null:
                return null;
            case NebraParser.PredicateReturnContext pr:
                return new TypePredicateRef(NewNodeID, SpanFromCtx(pr),
                    NameRefFromTerm(pr.NAME()), (TypeRef)Visit(pr.typeExpr()));
            case NebraParser.PlainReturnContext pl:
                return (TypeRef)Visit(pl.typeExpr());
            default:
                return null;
        }
    }

    private List<Parameter> VisitParamListContent(NebraParser.ParamListContext? ctx)
    {
        if (ctx == null) return [];

        switch (ctx)
        {
            case NebraParser.ParamListWithNamesContext withNames:
            {
                var result = withNames.param().Select(p =>
                {
                    var param = new Parameter(
                        NewNodeID,
                        NameRefFromTerm(p.NAME()),
                        VisitTypeAnnotationOpt(p.typeAnnotation()),
                        false,
                        p.expr() != null ? (Expr)Visit(p.expr()) : null,
                        SpanFromCtx(p));
                    param.Annotations = VisitAnnotationListContent(p.annotationList());
                    return param;
                }).ToList();

                if (withNames.varargParam() != null)
                {
                    var vp = withNames.varargParam();
                    var span = SpanFromCtx(vp);
                    var varargName = vp.NAME() != null ? NameRefFromTerm(vp.NAME()) : NameRefFromText("...", span);
                    result.Add(new Parameter(NewNodeID, varargName, VisitTypeAnnotationOpt(vp.typeAnnotation()), true, null, span));
                }

                return result;
            }
            case NebraParser.ParamListVarargContext vararg:
            {
                var vp = vararg.varargParam();
                var span = SpanFromCtx(vp);
                var varargName = vp.NAME() != null ? NameRefFromTerm(vp.NAME()) : NameRefFromText("...", span);
                return [new Parameter(NewNodeID, varargName, VisitTypeAnnotationOpt(vp.typeAnnotation()), true, null, span)];
            }
            default:
                return [];
        }
    }

    private (List<NameRef> namePath, NameRef? methodName) VisitFuncNameContent(NebraParser.FuncNameContext ctx)
    {
        var allNames = ctx.NAME().Select(NameRefFromTerm).ToList();
        NameRef? methodName = null;
        if (ctx.COLON() != null && allNames.Count > 0)
        {
            methodName = allNames[^1];
            allNames.RemoveAt(allNames.Count - 1);
        }

        return (allNames, methodName);
    }

    private List<AttribVar> VisitAttribNameListContent(NebraParser.AttribNameListContext ctx)
    {
        return ctx.attribName().Select(a => new AttribVar(
            NameRefFromTerm(a.NAME()),
            a.attrib()?.NAME()?.GetText(),
            VisitTypeAnnotationOpt(a.typeAnnotation()),
            SpanFromCtx(a)
        )).ToList();
    }

    /// <summary>
    /// Visits an <c>annotationList</c> grammar node and produces a list of <see cref="Annotation"/>
    /// IR nodes. Returns an empty list if the context is null or contains no annotations.
    /// </summary>
    private List<Annotation> VisitAnnotationListContent(NebraParser.AnnotationListContext? ctx)
    {
        if (ctx == null) return [];
        var result = new List<Annotation>();
        foreach (var a in ctx.annotation())
        {
            var name = NameRefFromTerm(a.NAME());
            var args = new List<AnnotationArg>();
            var argList = a.annotationArgList();
            if (argList != null)
            {
                foreach (var argCtx in argList.annotationArg())
                {
                    switch (argCtx)
                    {
                        case NebraParser.NamedAnnotationArgContext named:
                            args.Add(new AnnotationArg(
                                named.NAME().GetText(),
                                (Expr)Visit(named.expr()),
                                SpanFromCtx(named)));
                            break;
                        case NebraParser.PositionalAnnotationArgContext positional:
                            args.Add(new AnnotationArg(
                                null,
                                (Expr)Visit(positional.expr()),
                                SpanFromCtx(positional)));
                            break;
                    }
                }
            }
            result.Add(new Annotation(NewNodeID, SpanFromCtx(a), name, args));
        }
        return result;
    }

    private List<Expr> VisitArgsContent(NebraParser.ArgsContext ctx)
    {
        return ctx switch
        {
            NebraParser.ParenArgsContext paren =>
                paren.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [],
            NebraParser.TableArgsContext table =>
                [(Expr)Visit(table.tableConstructor())],
            NebraParser.StringArgsContext str =>
                [(Expr)Visit(str.str())],
            _ => []
        };
    }

    private Expr BuildSuffixChain(NebraParser.VarOrExpContext varOrExp, NebraParser.SuffixContext[] suffixes)
    {
        var result = (Expr)Visit(varOrExp);
        foreach (var suffix in suffixes)
            result = WrapWithSuffix(result, suffix);
        return result;
    }

    private Expr WrapWithSuffix(Expr obj, NebraParser.SuffixContext suffix)
    {
        TextSpan Span(ParserRuleContext ctx) => TextSpan.Combine(obj.Span, SpanFromCtx(ctx));

        return suffix switch
        {
            NebraParser.DotSuffixContext dot =>
                new DotAccessExpr(NewNodeID, Span(dot), obj, NameRefFromTerm(dot.NAME())),
            NebraParser.OptDotSuffixContext odot =>
                new DotAccessExpr(NewNodeID, Span(odot), obj, NameRefFromTerm(odot.NAME()), isOptional: true),
            NebraParser.IndexSuffixContext idx =>
                new IndexAccessExpr(NewNodeID, Span(idx), obj, (Expr)Visit(idx.expr())),
            NebraParser.MethodCallSuffixContext mc =>
                new MethodCallExpr(NewNodeID, Span(mc), obj, NameRefFromTerm(mc.NAME()), VisitArgsContent(mc.args())),
            NebraParser.CallSuffixContext call =>
                new FunctionCallExpr(NewNodeID, Span(call), obj, VisitArgsContent(call.args())),
            NebraParser.OptCallSuffixContext optCall =>
                new FunctionCallExpr(NewNodeID, Span(optCall), obj, VisitArgsContent(optCall.args()), isOptional: true),
            _ => throw new InvalidOperationException($"Unknown suffix type: {suffix.GetType().Name}")
        };
    }

    private NameRef NameRefFromTerm(ITerminalNode node)
    {
        return new NameRef(node.GetText(), SpanFromTerm(node));
    }
    
    private NameRef NameRefFromText(string name, TextSpan? span = null)
    {
        return new NameRef(name, span ?? TextSpan.Empty);
    }

    /// <summary>
    /// Maps a Nebra operator symbol (written after the <c>operator</c> keyword in a class)
    /// to its corresponding Lua metamethod name. Arity is used to disambiguate
    /// <c>-</c> (binary <c>__sub</c> vs. unary <c>__unm</c>).
    /// </summary>
    private static string? OperatorSymbolToMetamethod(string sym, int paramCount, out string? diagMessage)
    {
        diagMessage = null;
        switch (sym)
        {
            case "+": return paramCount == 1 ? "__add" : Err(out diagMessage, "binary '+' operator must take exactly one parameter");
            case "-":
                if (paramCount == 0) return "__unm";
                if (paramCount == 1) return "__sub";
                return Err(out diagMessage, "'-' operator must take zero (unary) or one (binary) parameter");
            case "*": return paramCount == 1 ? "__mul" : Err(out diagMessage, "binary '*' operator must take exactly one parameter");
            case "/": return paramCount == 1 ? "__div" : Err(out diagMessage, "binary '/' operator must take exactly one parameter");
            case "//": return paramCount == 1 ? "__idiv" : Err(out diagMessage, "binary '//' operator must take exactly one parameter");
            case "%": return paramCount == 1 ? "__mod" : Err(out diagMessage, "binary '%' operator must take exactly one parameter");
            case "^": return paramCount == 1 ? "__pow" : Err(out diagMessage, "binary '^' operator must take exactly one parameter");
            case "..": return paramCount == 1 ? "__concat" : Err(out diagMessage, "binary '..' operator must take exactly one parameter");
            case "==": return paramCount == 1 ? "__eq" : Err(out diagMessage, "binary '==' operator must take exactly one parameter");
            case "<": return paramCount == 1 ? "__lt" : Err(out diagMessage, "binary '<' operator must take exactly one parameter");
            case "<=": return paramCount == 1 ? "__le" : Err(out diagMessage, "binary '<=' operator must take exactly one parameter");
            case "#": return paramCount == 0 ? "__len" : Err(out diagMessage, "unary '#' operator must take no parameters");
            default: diagMessage = sym; return null;
        }

        static string? Err(out string? msg, string m) { msg = m; return null; }
    }

    /// <summary>
    /// Visits a <c>typeParamList</c> grammar node and produces a list of <see cref="TypeParamDef"/> IR nodes.
    /// Returns an empty list if the context is null.
    /// </summary>
    private List<TypeParamDef> VisitTypeParamListContent(NebraParser.TypeParamListContext? ctx)
    {
        if (ctx == null) return [];
        var result = new List<TypeParamDef>();
        foreach (var tp in ctx.typeParam())
        {
            var name = NameRefFromTerm(tp.NAME());
            TypeRef? extendsBound = null;
            var implementsBounds = new List<TypeRef>();
            var typeExprs = tp.typeExpr();
            var idx = 0;
            if (tp.EXTENDS() != null && typeExprs.Length > idx)
            {
                extendsBound = (TypeRef)Visit(typeExprs[idx]);
                idx++;
            }
            if (tp.IMPLEMENTS() != null)
            {
                for (; idx < typeExprs.Length; idx++)
                    implementsBounds.Add((TypeRef)Visit(typeExprs[idx]));
            }
            result.Add(new TypeParamDef(NewNodeID, SpanFromCtx(tp), name, extendsBound, implementsBounds));
        }
        return result;
    }

    /// <summary>
    /// Visits a <c>typeArgList</c> grammar node and produces a list of <see cref="TypeArgRef"/> IR nodes.
    /// Returns an empty list if the context is null.
    /// </summary>
    private List<TypeArgRef> VisitTypeArgListContent(NebraParser.TypeArgListContext? ctx)
    {
        if (ctx == null) return [];
        var result = new List<TypeArgRef>();
        foreach (var arg in ctx.typeArg())
        {
            switch (arg)
            {
                case NebraParser.ConcreteTypeArgContext cta:
                    result.Add(new ConcreteTypeArgRef(NewNodeID, SpanFromCtx(cta), (TypeRef)Visit(cta.typeExpr())));
                    break;
                case NebraParser.WildcardTypeArgContext wta:
                {
                    var kind = WildcardKind.Unbounded;
                    TypeRef? bound = null;
                    if (wta.EXTENDS() != null)
                    {
                        kind = WildcardKind.Extends;
                        bound = (TypeRef)Visit(wta.typeExpr());
                    }
                    else if (wta.SUPER() != null)
                    {
                        kind = WildcardKind.Super;
                        bound = (TypeRef)Visit(wta.typeExpr());
                    }
                    result.Add(new WildcardTypeArgRef(NewNodeID, SpanFromCtx(wta), kind, bound));
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Extracts the head <see cref="NameRef"/> from a <c>classRef</c> grammar node and returns the
    /// accompanying type arguments (empty list if none).
    /// </summary>
    private (NameRef name, List<TypeArgRef> typeArgs) VisitClassRefContent(NebraParser.ClassRefContext ctx)
    {
        var name = NameRefFromTerm(ctx.NAME());
        var typeArgs = VisitTypeArgListContent(ctx.typeArgList());
        return (name, typeArgs);
    }
}
