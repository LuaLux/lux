using Lux.Compiler;
using Lux.Configuration;
using Lux.IR;

namespace Lux.Doc;

/// <summary>
/// Walks every <c>.lux</c> file under the project's source root, compiles
/// them through the standard pipeline so doc comments and inferred types
/// are populated, and produces the format-agnostic <see cref="DocSite"/>
/// model that the markdown and HTML renderers consume.
/// </summary>
public static class DocSiteBuilder
{
    public static DocSite Build(Config config, string projectRoot, out List<string> diagnostics)
    {
        diagnostics = [];

        var srcDir = Path.IsPathRooted(config.Source) ? config.Source : Path.Combine(projectRoot, config.Source);
        if (!Directory.Exists(srcDir))
        {
            diagnostics.Add($"Source directory '{srcDir}' not found.");
            return new DocSite { ProjectName = config.Name ?? "lux project", ProjectVersion = config.Version };
        }

        var compiler = new LuxCompiler { Config = config.Clone() };
        var files = Directory.EnumerateFiles(srcDir, "*.lux", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
            compiler.AddSource(file);

        compiler.Compile();
        foreach (var d in compiler.Diagnostics.Diagnostics)
            if (d.Level == Lux.Diagnostics.DiagnosticLevel.Error)
                diagnostics.Add(d.ToString());

        var site = new DocSite
        {
            ProjectName = config.Name ?? "lux project",
            ProjectVersion = config.Version
        };

        foreach (var pkg in compiler.Packages.Values)
        {
            foreach (var file in pkg.Files)
            {
                if (file.IsDeclarationFile) continue;
                if (file.Filename == null) continue;
                if (!file.Filename.StartsWith(srcDir, StringComparison.OrdinalIgnoreCase)) continue;

                var module = BuildModule(pkg, file, srcDir);
                if (module.Symbols.Count > 0 || module.Doc != null)
                    site.Modules.Add(module);
            }
        }

        site.Modules.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return site;
    }

    private static DocModule BuildModule(PackageContext pkg, PreparsedFile file, string srcDir)
    {
        var slug = Path.GetRelativePath(srcDir, file.Filename!).Replace('\\', '/');
        if (slug.EndsWith(".lux", StringComparison.OrdinalIgnoreCase))
            slug = slug[..^4];

        var symbols = new List<DocSymbol>();
        DocComment? fileDoc = null;

        foreach (var stmt in file.Hir.Body)
        {
            if (stmt is ExportStmt export)
            {
                var sym = BuildSymbol(pkg, export.Declaration);
                if (sym != null) symbols.Add(sym);
            }
            else if (stmt is Decl topDecl && fileDoc == null && topDecl.Span.StartLn == 1 && topDecl.Doc != null)
            {
                fileDoc = topDecl.Doc;
            }
        }

        if (fileDoc == null)
            fileDoc = ExtractFileLevelDoc(file.Content);

        return new DocModule
        {
            Name = slug,
            FilePath = file.Filename!,
            Doc = fileDoc,
            Symbols = symbols
        };
    }

    /// <summary>
    /// Picks up a documentation comment that sits at the very top of the file,
    /// before any declaration, treating it as the module-level summary. Without
    /// this, files where the comment isn't directly attached to a decl (e.g.
    /// only a heading) would have no module description in the doc site.
    /// </summary>
    private static DocComment? ExtractFileLevelDoc(string source)
    {
        var lines = source.Split('\n');
        var collected = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r').TrimStart();
            if (line.StartsWith("---"))
            {
                var content = line[3..];
                if (content.Length > 0 && content[0] == ' ') content = content[1..];
                collected.Add(content);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { if (collected.Count > 0) break; else continue; }
            break;
        }
        return collected.Count > 0 ? DocCommentParser.Parse(collected) : null;
    }

    private static DocSymbol? BuildSymbol(PackageContext pkg, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl fd when fd.NamePath.Count > 0:
                return new DocSymbol
                {
                    Name = string.Join(".", fd.NamePath.Select(n => n.Name)) + (fd.MethodName != null ? ":" + fd.MethodName.Name : ""),
                    Kind = DocSymbolKind.Function,
                    Signature = FormatFunctionSignature(pkg, fd.NamePath[0], fd.Parameters, fd.IsAsync),
                    Doc = fd.Doc
                };
            case LocalFunctionDecl lfd:
                return new DocSymbol
                {
                    Name = lfd.Name.Name,
                    Kind = DocSymbolKind.Function,
                    Signature = FormatFunctionSignature(pkg, lfd.Name, lfd.Parameters, lfd.IsAsync),
                    Doc = lfd.Doc
                };
            case LocalDecl ld when ld.Variables.Count > 0:
                var v = ld.Variables[0];
                return new DocSymbol
                {
                    Name = v.Name.Name,
                    Kind = DocSymbolKind.Variable,
                    Signature = $"{v.Name.Name}: {FormatSymbolType(pkg, v.Name)}",
                    Doc = ld.Doc
                };
            case EnumDecl ed:
                return new DocSymbol
                {
                    Name = ed.Name.Name,
                    Kind = DocSymbolKind.Enum,
                    Signature = $"enum {ed.Name.Name}",
                    Doc = ed.Doc,
                    Members = ed.Members.Select(m => new DocMember
                    {
                        Name = m.Name.Name,
                        Kind = DocMemberKind.EnumMember,
                        Signature = m.Name.Name,
                        Doc = m.Doc
                    }).ToList()
                };
            case ClassDecl cd:
                return new DocSymbol
                {
                    Name = cd.Name.Name,
                    Kind = DocSymbolKind.Class,
                    Signature = (cd.IsAbstract ? "abstract " : "") + "class " + cd.Name.Name,
                    Doc = cd.Doc,
                    BaseTypes = cd.BaseClass != null ? [cd.BaseClass.Name] : [],
                    Implements = cd.Interfaces.Select(i => i.Name).ToList(),
                    Members = BuildClassMembers(pkg, cd)
                };
            case InterfaceDecl ifd:
                return new DocSymbol
                {
                    Name = ifd.Name.Name,
                    Kind = DocSymbolKind.Interface,
                    Signature = "interface " + ifd.Name.Name,
                    Doc = ifd.Doc,
                    BaseTypes = ifd.BaseInterfaces.Select(b => b.Name).ToList(),
                    Members = BuildInterfaceMembers(pkg, ifd)
                };
            default:
                return null;
        }
    }

    private static List<DocMember> BuildClassMembers(PackageContext pkg, ClassDecl cd)
    {
        var members = new List<DocMember>();
        foreach (var f in cd.Fields)
        {
            if (f.IsLocal) continue;
            members.Add(new DocMember
            {
                Name = f.Name.Name,
                Kind = DocMemberKind.Field,
                Signature = $"{f.Name.Name}: {FormatSymbolType(pkg, f.Name)}",
                Doc = f.Doc,
                IsStatic = f.IsStatic,
                IsProtected = f.IsProtected
            });
        }
        if (cd.Constructor != null)
        {
            members.Add(new DocMember
            {
                Name = "constructor",
                Kind = DocMemberKind.Constructor,
                Signature = $"constructor({FormatParams(pkg, cd.Constructor.Parameters)})",
                Doc = cd.Constructor.Doc
            });
        }
        foreach (var m in cd.Methods)
        {
            if (m.IsLocal) continue;
            members.Add(new DocMember
            {
                Name = m.Name.Name,
                Kind = DocMemberKind.Method,
                Signature = FormatFunctionSignature(pkg, m.Name, m.Parameters, m.IsAsync),
                Doc = m.Doc,
                IsStatic = m.IsStatic,
                IsProtected = m.IsProtected,
                IsAbstract = m.IsAbstract
            });
        }
        foreach (var a in cd.Accessors)
        {
            members.Add(new DocMember
            {
                Name = a.Name.Name,
                Kind = DocMemberKind.Accessor,
                Signature = (a.Kind == AccessorKind.Getter ? "get " : "set ") + a.Name.Name + "(" + FormatParams(pkg, a.Parameters) + ")",
            });
        }
        return members;
    }

    private static List<DocMember> BuildInterfaceMembers(PackageContext pkg, InterfaceDecl ifd)
    {
        var members = new List<DocMember>();
        foreach (var f in ifd.Fields)
        {
            members.Add(new DocMember
            {
                Name = f.Name.Name,
                Kind = DocMemberKind.Field,
                Signature = $"{f.Name.Name}: {FormatSymbolType(pkg, f.Name)}",
                Doc = f.Doc
            });
        }
        foreach (var m in ifd.Methods)
        {
            members.Add(new DocMember
            {
                Name = m.Name.Name,
                Kind = DocMemberKind.Method,
                Signature = FormatFunctionSignature(pkg, m.Name, m.Parameters, m.IsAsync),
                Doc = m.Doc
            });
        }
        return members;
    }

    private static string FormatFunctionSignature(PackageContext pkg, NameRef nameRef, List<Parameter> parameters, bool isAsync)
    {
        var paramStr = FormatParams(pkg, parameters);
        var ret = "nil";
        if (nameRef.Sym != SymID.Invalid && pkg.Syms.GetByID(nameRef.Sym, out var sym)
            && pkg.Types.GetByID(sym.Type, out var t) && t is FunctionType ft)
        {
            ret = FormatType(pkg, ft.ReturnType);
        }
        var prefix = isAsync ? "async function " : "function ";
        return $"{prefix}{nameRef.Name}({paramStr}): {ret}";
    }

    private static string FormatParams(PackageContext pkg, List<Parameter> parameters)
    {
        var parts = new List<string>();
        foreach (var p in parameters)
        {
            if (p.IsVararg)
            {
                var t = FormatSymbolType(pkg, p.Name);
                parts.Add(p.Name.Name == "..." ? $"...: {t}" : $"...{p.Name.Name}: {t}");
            }
            else
            {
                var t = FormatSymbolType(pkg, p.Name);
                var part = $"{p.Name.Name}: {t}";
                if (p.DefaultValue != null) part += " = ...";
                parts.Add(part);
            }
        }
        return string.Join(", ", parts);
    }

    private static string FormatSymbolType(PackageContext pkg, NameRef nameRef)
    {
        if (nameRef.Sym == SymID.Invalid) return "any";
        if (!pkg.Syms.GetByID(nameRef.Sym, out var sym)) return "any";
        if (sym.Type == TypID.Invalid) return "any";
        return FormatType(pkg, sym.Type);
    }

    private static string FormatType(PackageContext pkg, TypID id)
    {
        if (!pkg.Types.GetByID(id, out var t)) return "any";
        return FormatType(pkg, t);
    }

    private static string FormatType(PackageContext pkg, IR.Type t)
    {
        return t switch
        {
            FunctionType ft => $"({string.Join(", ", ft.ParamTypes.Select(p => FormatType(pkg, p)))}) -> {FormatType(pkg, ft.ReturnType)}",
            TableArrayType arr => $"{FormatType(pkg, arr.ElementType)}[]",
            TableMapType map => $"{{ [{FormatType(pkg, map.KeyType)}]: {FormatType(pkg, map.ValueType)} }}",
            UnionType union => string.Join(" | ", union.Types.Select(u => FormatType(pkg, u))),
            StructType st => $"{{ {string.Join(", ", st.Fields.Select(f => $"{f.Name.Name}: {FormatType(pkg, f.Type)}"))} }}",
            TupleType tu => $"({string.Join(", ", tu.Fields.Select(f => FormatType(pkg, f.Type)))})",
            ClassType c => c.Name,
            InterfaceType i => i.Name,
            EnumType e => e.Name,
            TypeParameterType tp => tp.Name,
            _ => t.Kind switch
            {
                TypeKind.PrimitiveString => "string",
                TypeKind.PrimitiveNumber => "number",
                TypeKind.PrimitiveBool => "boolean",
                TypeKind.PrimitiveNil => "nil",
                TypeKind.PrimitiveAny => "any",
                TypeKind.PrimitiveFunction => "function",
                TypeKind.PrimitiveThread => "thread",
                TypeKind.PrimitiveUserdata => "userdata",
                TypeKind.PrimitiveNever => "never",
                _ => t.Kind.ToString().ToLowerInvariant()
            }
        };
    }
}
