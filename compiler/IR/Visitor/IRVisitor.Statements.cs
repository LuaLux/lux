namespace Nebra.IR;

internal partial class IRVisitor
{
    #region Delegating stmt alternatives

    public override Node VisitEmptyStat(NebraParser.EmptyStatContext context) => null!;

    public override Node VisitAssignStat(NebraParser.AssignStatContext context)
    {
        var targets = context.varList().var().Select(v => (Expr)Visit(v)).ToList();
        var values = context.exprList().expr().Select(e => (Expr)Visit(e)).ToList();
        return new AssignStmt(NewNodeID, SpanFromCtx(context), targets, values);
    }

    public override Node VisitFunctionCallStat(NebraParser.FunctionCallStatContext context)
        => new ExprStmt(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.functionCall()));

    public override Node VisitNewStat(NebraParser.NewStatContext context)
    {
        var className = NameRefFromTerm(context.NAME());
        var args = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        var span = SpanFromCtx(context);
        return new ExprStmt(NewNodeID, span,
            new NewExpr(NewNodeID, span, className, args));
    }

    public override Node VisitIncDecStat_(NebraParser.IncDecStat_Context context) => Visit(context.incDecStat());

    public override Node VisitPostIncStat(NebraParser.PostIncStatContext context)
        => new ExprStmt(NewNodeID, SpanFromCtx(context),
            new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.var()), isPre: false, isIncrement: true));

    public override Node VisitPostDecStat(NebraParser.PostDecStatContext context)
        => new ExprStmt(NewNodeID, SpanFromCtx(context),
            new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.var()), isPre: false, isIncrement: false));

    public override Node VisitPreIncStat(NebraParser.PreIncStatContext context)
        => new ExprStmt(NewNodeID, SpanFromCtx(context),
            new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.var()), isPre: true, isIncrement: true));

    public override Node VisitPreDecStat(NebraParser.PreDecStatContext context)
        => new ExprStmt(NewNodeID, SpanFromCtx(context),
            new IncDecExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.var()), isPre: true, isIncrement: false));

    public override Node VisitLabelStat(NebraParser.LabelStatContext context) => Visit(context.label());
    public override Node VisitBreakStat(NebraParser.BreakStatContext context)
    {
        var depth = 1;
        if (context.INT() != null && int.TryParse(context.INT().GetText(), out var d)) depth = d;
        return new BreakStmt(NewNodeID, SpanFromCtx(context), depth);
    }

    public override Node VisitContinueStat(NebraParser.ContinueStatContext context)
        => new ContinueStmt(NewNodeID, SpanFromCtx(context));

    public override Node VisitDeferStat_(NebraParser.DeferStat_Context context) => Visit(context.deferStat());

    public override Node VisitDeferCallStat(NebraParser.DeferCallStatContext context)
        => new DeferStmt(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.functionCall()), null);

    public override Node VisitDeferBlockStat(NebraParser.DeferBlockStatContext context)
    {
        var body = VisitBranchBlock(context.doBlock().block());
        return new DeferStmt(NewNodeID, SpanFromCtx(context), null, body);
    }

    public override Node VisitGuardStat_(NebraParser.GuardStat_Context context) => Visit(context.guardStat());

    public override Node VisitGuardStat(NebraParser.GuardStatContext context)
    {
        var exprs = context.expr();
        var condition = (Expr)Visit(exprs[0]);
        Expr? elseExpr = exprs.Length > 1 ? (Expr)Visit(exprs[1]) : null;
        return new GuardStmt(NewNodeID, SpanFromCtx(context), condition, elseExpr);
    }
    public override Node VisitGotoStat(NebraParser.GotoStatContext context) => new GotoStmt(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()));
    public override Node VisitDoStat(NebraParser.DoStatContext context) => Visit(context.doBlock());
    public override Node VisitWhileStat(NebraParser.WhileStatContext context) => Visit(context.whileLoop());
    public override Node VisitRepeatStat(NebraParser.RepeatStatContext context) => Visit(context.repeatLoop());
    public override Node VisitIfStat_(NebraParser.IfStat_Context context) => Visit(context.ifStat());
    public override Node VisitNumericForStat(NebraParser.NumericForStatContext context) => Visit(context.numericFor());
    public override Node VisitGenericForStat(NebraParser.GenericForStatContext context) => Visit(context.genericFor());
    public override Node VisitFunctionDeclStat(NebraParser.FunctionDeclStatContext context) => Visit(context.functionDecl());
    public override Node VisitLocalFunctionDeclStat(NebraParser.LocalFunctionDeclStatContext context) => Visit(context.localFunctionDecl());
    public override Node VisitLocalDeclStat(NebraParser.LocalDeclStatContext context) => Visit(context.localDecl());

    public override Node VisitEnumDeclStat(NebraParser.EnumDeclStatContext context) => Visit(context.enumDecl());
    public override Node VisitImportStat_(NebraParser.ImportStat_Context context) => Visit(context.importStat());
    public override Node VisitExportStat_(NebraParser.ExportStat_Context context) => Visit(context.exportStat());
    public override Node VisitDeclareStat_(NebraParser.DeclareStat_Context context) => Visit(context.declareStat());
    public override Node VisitMatchStat_(NebraParser.MatchStat_Context context) => Visit(context.matchStat());
    public override Node VisitClassDeclStat(NebraParser.ClassDeclStatContext context) => Visit(context.classDecl());

    public override Node VisitSuperCallStat(NebraParser.SuperCallStatContext context)
    {
        var args = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        return new ExprStmt(NewNodeID, SpanFromCtx(context), new SuperCallExpr(NewNodeID, SpanFromCtx(context), args));
    }
    public override Node VisitInterfaceDeclStat(NebraParser.InterfaceDeclStatContext context) => Visit(context.interfaceDecl());

    public override Node VisitExtendDeclStat(NebraParser.ExtendDeclStatContext context) => Visit(context.extendDecl());

    public override Node VisitMatchStat(NebraParser.MatchStatContext context)
    {
        var scrutinee = (Expr)Visit(context.expr());
        var arms = context.matchArm().Select(VisitMatchArmNode).ToList();
        return new MatchStmt(NewNodeID, SpanFromCtx(context), scrutinee, arms);
    }

    public override Node VisitMatchExprExpr(NebraParser.MatchExprExprContext context) => Visit(context.matchExpr());

    public override Node VisitMatchExpr(NebraParser.MatchExprContext context)
    {
        var scrutinee = (Expr)Visit(context.expr());
        var arms = context.matchExprArm().Select(VisitMatchExprArmNode).ToList();
        return new MatchExpr(NewNodeID, SpanFromCtx(context), scrutinee, arms);
    }

    private MatchArm VisitMatchArmNode(NebraParser.MatchArmContext ctx)
    {
        var pattern = VisitMatchPatternNode(ctx.matchPattern());
        var guard = ctx.WHEN() != null ? (Expr)Visit(ctx.expr()) : null;
        var (body, ret) = VisitBlockContent(ctx.block());
        if (ret != null) body.Add(ret);
        return new MatchArm(pattern, guard, body, SpanFromCtx(ctx));
    }


    private MatchExprArm VisitMatchExprArmNode(NebraParser.MatchExprArmContext ctx)
    {
        var pattern = VisitMatchPatternNode(ctx.matchPattern());
        var exprs = ctx.expr();
        Expr? guard = null;
        if (ctx.WHEN() != null)
        {
            guard = (Expr)Visit(exprs[0]);
        }
        var value = (Expr)Visit(exprs[^1]);
        return new MatchExprArm(pattern, guard, value, SpanFromCtx(ctx));
    }

    private MatchPattern VisitMatchPatternNode(NebraParser.MatchPatternContext ctx)
    {
        if (ctx is NebraParser.BindingPatternContext bp)
        {
            var name = NameRefFromTerm(bp.NAME());
            var typeRef = (TypeRef)Visit(bp.typeAnnotation().typeExpr());
            return new MatchPattern(MatchPatternKind.TypeBinding, null, typeRef, name, SpanFromCtx(ctx));
        }

        var vp = (NebraParser.ValuePatternContext)ctx;
        var expr = (Expr)Visit(vp.expr());
        if (expr is NameExpr ne && ne.Name.Name == "_")
            return new MatchPattern(MatchPatternKind.Wildcard, null, null, null, SpanFromCtx(ctx));

        return new MatchPattern(MatchPatternKind.Value, expr, null, null, SpanFromCtx(ctx));
    }

    #endregion

    public override Node VisitDoBlock(NebraParser.DoBlockContext context)
    {
        var body = VisitBranchBlock(context.block());
        return new DoBlockStmt(NewNodeID, SpanFromCtx(context), body);
    }

    public override Node VisitWhileLoop(NebraParser.WhileLoopContext context)
    {
        var condition = (Expr)Visit(context.expr());
        var body = VisitBranchBlock(context.block());
        return new WhileStmt(NewNodeID, SpanFromCtx(context), condition, body);
    }

    public override Node VisitRepeatLoop(NebraParser.RepeatLoopContext context)
    {
        var body = VisitBranchBlock(context.block());
        var condition = (Expr)Visit(context.expr());
        return new RepeatStmt(NewNodeID, SpanFromCtx(context), body, condition);
    }

    public override Node VisitIfStat(NebraParser.IfStatContext context)
    {
        var condition = (Expr)Visit(context.expr());
        var body = VisitBranchBlock(context.block());

        var elseIfs = context.elseIfClause().Select(eic =>
        {
            var eifCond = (Expr)Visit(eic.expr());
            var eifBody = VisitBranchBlock(eic.block());
            return new ElseIfClause(eifCond, eifBody, SpanFromCtx(eic));
        }).ToList();

        List<Stmt>? elseBody = null;
        if (context.elseClause() != null)
        {
            elseBody = VisitBranchBlock(context.elseClause().block());
        }

        return new IfStmt(NewNodeID, SpanFromCtx(context), condition, body, elseIfs, elseBody);
    }

    /// <summary>
    /// Walks a block context the way control-flow branches need it: the
    /// trailing <c>return</c> (which Lua's grammar treats as block-terminator)
    /// is folded into the regular statement list so it survives codegen. Bare
    /// <see cref="VisitBlockContent"/> drops it because top-level scripts and
    /// function bodies model the return separately.
    /// </summary>
    private List<Stmt> VisitBranchBlock(NebraParser.BlockContext ctx)
    {
        var (body, ret) = VisitBlockContent(ctx);
        if (ret != null) body.Add(ret);
        return body;
    }

    public override Node VisitNumericFor(NebraParser.NumericForContext context)
    {
        var exprs = context.expr();
        var start = (Expr)Visit(exprs[0]);
        var limit = (Expr)Visit(exprs[1]);
        Expr? step = exprs.Length > 2 ? (Expr)Visit(exprs[2]) : null;
        var body = VisitBranchBlock(context.block());
        return new NumericForStmt(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), start, limit, step, body);
    }

    public override Node VisitGenericFor(NebraParser.GenericForContext context)
    {
        var varNames = context.nameList().NAME().Select(NameRefFromTerm).ToList();
        var iterators = context.exprList().expr().Select(e => (Expr)Visit(e)).ToList();
        var body = VisitBranchBlock(context.block());
        return new GenericForStmt(NewNodeID, SpanFromCtx(context), varNames, iterators, body);
    }

    public override Node VisitLabel(NebraParser.LabelContext context)
        => new LabelStmt(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()));

    public override Node VisitReturnStat(NebraParser.ReturnStatContext context)
    {
        var values = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        return new ReturnStmt(NewNodeID, SpanFromCtx(context), values);
    }
    
    public override Node VisitImportFrom(NebraParser.ImportFromContext context)
    {
        var module = NameRefFromString(context.str());
        var body = context.importBody();

        return body switch
        {
            NebraParser.NamedImportContext named => new ImportStmt(NewNodeID, SpanFromCtx(context), ImportKind.Named,
                module)
            {
                Specifiers = named.importName().Select(n =>
                {
                    var names = n.NAME();
                    return new ImportSpecifier(
                        NewNodeID,
                        NameRefFromTerm(names[0]),
                        names.Length > 1 ? NameRefFromTerm(names[1]) : null,
                        SpanFromCtx(n)
                    );
                }).ToList()
            },
            NebraParser.DefaultImportContext def => new ImportStmt(NewNodeID, SpanFromCtx(context),
                ImportKind.Default, module)
            {
                Alias = NameRefFromTerm(def.NAME())
            },
            NebraParser.NamespaceImportContext ns => new ImportStmt(NewNodeID, SpanFromCtx(context),
                ImportKind.Namespace, module)
            {
                Alias = NameRefFromTerm(ns.NAME())
            },
            _ => throw new InvalidOperationException($"Unknown import body type: {body.GetType().Name}")
        };
    }

    public override Node VisitImportSideEffect(NebraParser.ImportSideEffectContext context)
        => new ImportStmt(NewNodeID, SpanFromCtx(context), ImportKind.SideEffect, NameRefFromString(context.str()));
    
    public override Node VisitExportFunction(NebraParser.ExportFunctionContext context)
    {
        var decl = (Decl)Visit(context.functionDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }

    public override Node VisitExportLocalFunction(NebraParser.ExportLocalFunctionContext context)
    {
        var decl = (Decl)Visit(context.localFunctionDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }

    public override Node VisitExportLocal(NebraParser.ExportLocalContext context)
    {
        var decl = (Decl)Visit(context.localDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }

    public override Node VisitExportEnum(NebraParser.ExportEnumContext context)
    {
        var decl = (Decl)Visit(context.enumDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }

    public override Node VisitExportClass(NebraParser.ExportClassContext context)
    {
        var decl = (Decl)Visit(context.classDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }

    public override Node VisitExportInterface(NebraParser.ExportInterfaceContext context)
    {
        var decl = (Decl)Visit(context.interfaceDecl());
        return new ExportStmt(NewNodeID, SpanFromCtx(context), decl);
    }
}
