using Nebra.IR;

namespace Nebra.Compiler.Passes;

public sealed class ResolveImportsPass() : Pass(PassName, PassScope.PerBuild)
{
    public const string PassName = "ResolveImports";

    private ModuleResolver? _resolver;

    public override bool Run(PassContext context)
    {
        _resolver ??= new ModuleResolver(context.Config);

        var newFiles = new List<PreparsedFile>();

        var preexisting = new HashSet<PreparsedFile>();
        foreach (var pkg in context.Pkgs)
            foreach (var f in pkg.Files) preexisting.Add(f);

        foreach (var pkg in context.Pkgs)
        {
            var filesSnapshot = pkg.Files.ToList();
            foreach (var file in filesSnapshot)
            {
                ProcessFileImports(context, pkg, file, newFiles);
            }
        }

        var freshlyInjected = newFiles.Where(f => !preexisting.Contains(f)).ToList();
        if (freshlyInjected.Count > 0)
            BindAndResolveNewFiles(context, freshlyInjected);

        return true;
    }

    private void ProcessFileImports(PassContext ctx, PackageContext pkg, PreparsedFile file,
        List<PreparsedFile> newFiles)
    {
        foreach (var stmt in file.Hir.Body)
        {
            if (stmt is not ImportStmt import) continue;

            var resolved = _resolver!.Resolve(
                import.Module.Name, file.Filename, ctx.Pkgs, ctx.Diag, ctx.NodeAlloc);

            if (resolved == null)
            {
                if (!import.IsTypeOnly)
                    ctx.Diag.Report(import.Module.Span, Diagnostics.DiagnosticCode.ErrModuleNotFound, import.Module.Name);
                continue;
            }

            ctx.Cache[$"import_resolved:{file.Filename}:{import.Module.Name}"] = resolved;

            if (resolved is { Kind: ModuleKind.NebraSource or ModuleKind.Declaration, File: not null })
            {
                if (!newFiles.Contains(resolved.File))
                    newFiles.Add(resolved.File);
            }
        }
    }

    /// <summary>
    /// Runs <see cref="BindDeclarePass"/> on each freshly-loaded library file
    /// inside its own sub-scope of the package root. The sub-scope keeps the
    /// library's <c>export</c> names from clashing with the consumer's
    /// <c>import { … }</c> declarations (which BindDeclare put into the package
    /// root earlier in this pass cycle); <see cref="ResolveFromNebraSource"/>
    /// then walks the sub-scope to copy types onto the import bindings.
    /// </summary>
    private static void BindAndResolveNewFiles(PassContext ctx, List<PreparsedFile> newFiles)
    {
        var bindPass = new BindDeclarePass();

        foreach (var pkg in ctx.Pkgs)
        {
            foreach (var file in newFiles)
            {
                if (!pkg.Files.Contains(file)) continue;

                if (file.BindingScopeOverride == null)
                    file.BindingScopeOverride = pkg.Scopes.NewScope(pkg.Root);

                var fileCtx = new PassContext(ctx.Diag, ctx.Pkgs, pkg, file, ctx.Types,
                    ctx.SymAlloc, ctx.ScopeAlloc, ctx.NodeAlloc, ctx.Names, ctx.Cache, ctx.Config);
                bindPass.Run(fileCtx);
            }
        }
    }

    /// <summary>
    /// Walks each import statement in <paramref name="file"/> and copies the
    /// resolved type from the source symbol onto the importer's symbol.
    /// Invoked from <see cref="ResolveTypeRefsPass"/> after class/interface/enum
    /// types have been pre-declared, so cross-file <c>import { Vec2 }</c> sees a
    /// fully-built source type at copy time.
    /// </summary>
    public static void PropagateImportTypes(PassContext ctx, PackageContext pkg, PreparsedFile file)
    {
        foreach (var stmt in file.Hir.Body)
        {
            if (stmt is not ImportStmt import) continue;

            var cacheKey = $"import_resolved:{file.Filename}:{import.Module.Name}";
            if (!ctx.Cache.TryGetValue(cacheKey, out var obj) || obj is not ResolvedModule resolved)
                continue;

            switch (resolved.Kind)
            {
                case ModuleKind.DeclareModule:
                    ResolveFromDeclareModule(ctx, pkg, import, resolved.DeclareModule!);
                    break;
                case ModuleKind.Declaration:
                {
                    var sourcePkg = FindPackageOf(ctx, resolved.File!) ?? pkg;
                    ResolveFromDeclFile(ctx, pkg, sourcePkg, import, resolved.File!);
                    break;
                }
                case ModuleKind.NebraSource:
                {
                    var sourcePkg = FindPackageOf(ctx, resolved.File!) ?? pkg;
                    ResolveFromNebraSource(ctx, pkg, sourcePkg, import, resolved.File!);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Each <c>.neb</c> source file lives in its own <see cref="PackageContext"/>
    /// (see <c>NebraCompiler.AddSource</c>), so a cross-file import has to look up
    /// the source symbol in the EXPORTER's package, not the importer's. This
    /// helper finds the package that owns <paramref name="file"/>.
    /// </summary>
    private static PackageContext? FindPackageOf(PassContext ctx, PreparsedFile file)
    {
        foreach (var p in ctx.Pkgs)
        {
            if (p.Files.Contains(file)) return p;
        }
        return null;
    }

    private static void ResolveFromDeclareModule(PassContext ctx, PackageContext pkg,
        ImportStmt import, DeclareModuleDecl declModule)
    {
        if (!pkg.Scopes.EnclosingScope(declModule.ID, out var moduleScope))
            return;

        switch (import.Kind)
        {
            case ImportKind.Named:
                foreach (var spec in import.Specifiers)
                {
                    var memberName = spec.Name.Name;
                    if (pkg.Scopes.LookupOnlyCurrent(moduleScope, memberName, out var memberSym))
                    {
                        var importName = spec.Alias ?? spec.Name;
                        CopySymbolType(pkg, memberSym, importName.Sym);
                    }
                }
                break;
            case ImportKind.Default:
            case ImportKind.Namespace:
                if (import.Alias != null)
                {
                    var moduleSym = FindModuleSymbol(pkg, declModule.ModuleName.Name);
                    if (moduleSym != SymID.Invalid)
                        CopySymbolType(pkg, moduleSym, import.Alias.Sym);
                }
                break;
        }
    }

    private static void ResolveFromDeclFile(PassContext ctx, PackageContext pkg, PackageContext sourcePkg,
        ImportStmt import, PreparsedFile declFile)
    {
        foreach (var stmt in declFile.Hir.Body)
        {
            if (stmt is DeclareModuleDecl dmd && dmd.ModuleName.Name == import.Module.Name)
            {
                ResolveFromDeclareModule(ctx, pkg, import, dmd);
                return;
            }
        }

        ResolveFromTopLevelDeclarations(ctx, pkg, sourcePkg, import, declFile);
    }

    private static void ResolveFromNebraSource(PassContext ctx, PackageContext pkg, PackageContext sourcePkg,
        ImportStmt import, PreparsedFile sourceFile)
    {
        // exports live in the SOURCE file's package (each .neb file gets its
        // own package); use sourcePkg for the lookups, pkg for the target.
        var exports = CollectExportedSymbols(sourcePkg, sourceFile);

        switch (import.Kind)
        {
            case ImportKind.Named:
                foreach (var spec in import.Specifiers)
                {
                    var memberName = spec.Name.Name;
                    if (exports.TryGetValue(memberName, out var exportSym))
                    {
                        var importName = spec.Alias ?? spec.Name;
                        CopySymbolType(sourcePkg, exportSym, pkg, importName.Sym);
                        continue;
                    }

                    ReportMissingImportMember(ctx, pkg, sourcePkg, import, sourceFile, spec);
                }
                break;
            case ImportKind.Default:
            case ImportKind.Namespace:
                break;
        }
    }

    /// <summary>
    /// Reports a named import the source module does not provide, separating a member that exists
    /// but was never exported from one that does not exist at all. Without this the binding is
    /// left untyped and the name resolves to nil at runtime. Guarded through the pass cache
    /// because import types are propagated again after every package is typed, and the same
    /// specifier would otherwise be reported once per round.
    /// </summary>
    private static void ReportMissingImportMember(PassContext ctx, PackageContext pkg, PackageContext sourcePkg,
        ImportStmt import, PreparsedFile sourceFile, ImportSpecifier spec)
    {
        var key = $"import_member_missing:{pkg.Path}:{import.Module.Name}:{spec.Name.Name}";
        if (!ctx.Cache.TryAdd(key, true)) return;

        var lookupScope = sourceFile.BindingScopeOverride ?? sourcePkg.Root;
        var code = sourcePkg.Scopes.Lookup(lookupScope, spec.Name.Name, out _)
            ? Diagnostics.DiagnosticCode.ErrSymbolNotExported
            : Diagnostics.DiagnosticCode.ErrSymbolNotFound;

        ctx.Diag.Report(spec.Name.Span, code, spec.Name.Name, import.Module.Name);
    }

    private static void ResolveFromTopLevelDeclarations(PassContext ctx, PackageContext pkg, PackageContext sourcePkg,
        ImportStmt import, PreparsedFile file)
    {
        var topLevel = new Dictionary<string, SymID>();

        foreach (var stmt in file.Hir.Body)
        {
            switch (stmt)
            {
                case DeclareFunctionDecl dfd when dfd.NamePath.Count == 1:
                    if (sourcePkg.Scopes.Lookup(sourcePkg.Root, dfd.NamePath[0].Name, out var dfSym))
                        topLevel[dfd.NamePath[0].Name] = dfSym;
                    break;
                case DeclareVariableDecl dvd:
                    if (sourcePkg.Scopes.Lookup(sourcePkg.Root, dvd.Name.Name, out var dvSym))
                        topLevel[dvd.Name.Name] = dvSym;
                    break;
            }
        }

        if (topLevel.Count == 0) return;

        switch (import.Kind)
        {
            case ImportKind.Named:
                foreach (var spec in import.Specifiers)
                {
                    if (topLevel.TryGetValue(spec.Name.Name, out var sym))
                    {
                        var importName = spec.Alias ?? spec.Name;
                        CopySymbolType(sourcePkg, sym, pkg, importName.Sym);
                    }
                }
                break;
        }
    }

    private static Dictionary<string, SymID> CollectExportedSymbols(PackageContext pkg, PreparsedFile file)
    {
        var exports = new Dictionary<string, SymID>();
        var lookupScope = file.BindingScopeOverride ?? pkg.Root;

        foreach (var stmt in file.Hir.Body)
        {
            if (stmt is not ExportStmt export) continue;

            switch (export.Declaration)
            {
                case FunctionDecl { NamePath.Count: > 0 } fd:
                {
                    var name = fd.NamePath[0].Name;
                    if (fd.NamePath[0].Sym != SymID.Invalid)
                        exports[name] = fd.NamePath[0].Sym;
                    else if (pkg.Scopes.Lookup(lookupScope, name, out var sym))
                        exports[name] = sym;
                    break;
                }
                case LocalFunctionDecl lfd:
                {
                    if (lfd.Name.Sym != SymID.Invalid)
                        exports[lfd.Name.Name] = lfd.Name.Sym;
                    else if (pkg.Scopes.Lookup(lookupScope, lfd.Name.Name, out var sym))
                        exports[lfd.Name.Name] = sym;
                    break;
                }
                case LocalDecl ld:
                {
                    foreach (var v in ld.Variables)
                    {
                        if (v.Name.Sym != SymID.Invalid)
                            exports[v.Name.Name] = v.Name.Sym;
                        else if (pkg.Scopes.Lookup(lookupScope, v.Name.Name, out var sym))
                            exports[v.Name.Name] = sym;
                    }
                    break;
                }
                case ClassDecl cd:
                {
                    if (cd.Name.Sym != SymID.Invalid)
                        exports[cd.Name.Name] = cd.Name.Sym;
                    else if (pkg.Scopes.Lookup(lookupScope, cd.Name.Name, out var sym))
                        exports[cd.Name.Name] = sym;
                    break;
                }
                case InterfaceDecl id:
                {
                    if (id.Name.Sym != SymID.Invalid)
                        exports[id.Name.Name] = id.Name.Sym;
                    else if (pkg.Scopes.Lookup(lookupScope, id.Name.Name, out var sym))
                        exports[id.Name.Name] = sym;
                    break;
                }
                case EnumDecl ed:
                {
                    if (ed.Name.Sym != SymID.Invalid)
                        exports[ed.Name.Name] = ed.Name.Sym;
                    else if (pkg.Scopes.Lookup(lookupScope, ed.Name.Name, out var sym))
                        exports[ed.Name.Name] = sym;
                    break;
                }
            }
        }

        return exports;
    }

    /// <summary>
    /// Copies the resolved type of <paramref name="source"/> (looked up in
    /// <paramref name="srcPkg"/>'s symbol arena) onto <paramref name="target"/>
    /// (looked up in <paramref name="tgtPkg"/>'s arena). The two arenas can be
    /// the same (intra-package import) or different (cross-package import,
    /// e.g. between two sibling source files); SymIDs are unique across the
    /// shared <c>SymAlloc</c> but every <see cref="PackageContext"/> only
    /// stores its own subset of <see cref="Symbol"/> records.
    /// </summary>
    private static void CopySymbolType(PackageContext srcPkg, SymID source, PackageContext tgtPkg, SymID target)
    {
        if (source == SymID.Invalid || target == SymID.Invalid) return;
        if (!srcPkg.Syms.GetByID(source, out var srcSym)) return;
        if (!tgtPkg.Syms.GetByID(target, out var tgtSym)) return;
        if (srcSym.Type != TypID.Invalid)
            tgtSym.Type = srcSym.Type;
        tgtSym.Side = srcSym.Side;
    }

    private static void CopySymbolType(PackageContext pkg, SymID source, SymID target)
        => CopySymbolType(pkg, source, pkg, target);

    private static SymID FindModuleSymbol(PackageContext pkg, string name)
    {
        if (pkg.Scopes.Lookup(pkg.Root, name, out var sym))
            return sym;
        return SymID.Invalid;
    }
}
