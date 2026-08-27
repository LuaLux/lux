using System.Text;
using Nebra.Compiler.Codegen;
using Nebra.Configuration;
using Nebra.IR;
using Type = Nebra.IR.Type;

namespace Nebra.Compiler.Passes;

public sealed partial class CodegenPass
{
    private const string ReflectPrelude =
        """
        if not _G.reflect then
          local R = _G.__nebra_reflect or {}
          _G.__nebra_reflect = R
          local reflect = {}
          function reflect.get(id) return R[id] end
          function reflect.all() local t = {} for _, d in pairs(R) do t[#t + 1] = d end return t end
          local function byKind(k) local t = {} for _, d in pairs(R) do if d.kind == k then t[#t + 1] = d end end return t end
          function reflect.classes() return byKind("class") end
          function reflect.interfaces() return byKind("interface") end
          function reflect.enums() return byKind("enum") end
          function reflect.functions() return byKind("function") end
          function reflect.variables() return byKind("variable") end
          function reflect.typeOf(v)
            if type(v) == "table" then
              local mt = getmetatable(v)
              if mt and mt.__nebra then return R[mt.__nebra] end
              if v.__nebra then return R[v.__nebra] end
            end
            return { kind = "primitive", name = type(v) }
          end
          function reflect.dynamic(v) return reflect.typeOf(v) end
          function reflect.ref(id) local d = R[id] return d and d.ref end
          function reflect.create(info, ...) return info.ref.new(...) end
          function reflect.invoke(info, ...) return info.ref(...) end
          function reflect.implementorsOf(id)
            local t = {}
            for _, d in pairs(R) do
              if d.kind == "class" and d.interfaces then
                for _, i in ipairs(d.interfaces) do if i == id then t[#t + 1] = d break end end
              end
            end
            return t
          end
          function reflect.annotationsOf(d) return d.annotations or {} end
          function reflect.annotation(d, name)
            for _, a in ipairs(d.annotations or {}) do if a.name == name then return a end end
            return nil
          end
          function reflect.hasAnnotation(d, name) return reflect.annotation(d, name) ~= nil end
          _G.reflect = reflect
        end
        """;

    /// <summary>
    /// Emits the runtime <c>reflect</c> reader library and the shared registry table — guarded so it
    /// only initialises once per Lua runtime. Skipped entirely when reflection is off.
    /// </summary>
    private void EmitReflectionPrelude(PassContext ctx, LuaGenerator gen)
    {
        if (ctx.Config.Reflection.Mode == ReflectionMode.None) return;
        gen.Write(ReflectPrelude);
        gen.NewLine();
        gen.Write("local __nebra = _G.__nebra_reflect;");
        gen.NewLine();
    }

    /// <summary>
    /// Emits the reflection descriptor for a single top-level declaration, immediately after it is
    /// emitted, so later user code (including reflection queries) sees the registered metadata and
    /// <c>.__nebra</c> stamp. Each descriptor is a Lua table literal written verbatim.
    /// </summary>
    private void EmitReflectionFor(PassContext ctx, PackageContext pkg, LuaGenerator gen, Stmt stmt)
    {
        if (ctx.Config.Reflection.Mode == ReflectionMode.None) return;

        var decl = stmt is ExportStmt es ? es.Declaration : stmt as Decl;
        switch (decl)
        {
            case ClassDecl { IsDeclare: false } cd when IsReflectable(ctx, cd.Annotations):
                WriteBlock(gen, ClassMeta(ctx, pkg, cd));
                break;
            case InterfaceDecl { IsDeclare: false } id when IsReflectable(ctx, id.Annotations):
                WriteBlock(gen, InterfaceMeta(ctx, pkg, id));
                break;
            case EnumDecl ed when IsReflectable(ctx, ed.Annotations):
                WriteBlock(gen, EnumMeta(ctx, pkg, ed));
                break;
            case FunctionDecl { NamePath.Count: 1, MethodName: null } fd when IsReflectable(ctx, fd.Annotations):
                WriteBlock(gen, FunctionMeta(ctx, pkg, fd));
                break;
            case LocalDecl ld:
                foreach (var v in ld.Variables) WriteBlock(gen, VariableMeta(ctx, pkg, v));
                break;
        }
    }

    /// <summary>
    /// Lowers the <c>reflect.Of(x)</c> intrinsic using the static type of <c>x</c>: a class/interface/
    /// enum (or a bare type name) becomes a constant <c>reflect.get("id")</c>; a primitive/composite
    /// becomes a constant <c>TypeDesc</c>; an <c>any</c> argument falls back to a runtime
    /// <c>reflect.dynamic(x)</c> dispatch. Returns false when the call is not <c>reflect.Of</c>.
    /// </summary>
    private bool TryEmitReflectOf(PassContext ctx, PackageContext pkg, LuaGenerator gen, FunctionCallExpr call)
    {
        if (call.Callee is not DotAccessExpr { Object: NameExpr { Name.Name: "reflect" }, FieldName.Name: "Of" }) return false;
        if (call.Arguments.Count != 1) return false;
        var arg = call.Arguments[0];

        if (arg is NameExpr ne && ne.Name.Sym != SymID.Invalid && pkg.Syms.GetByID(ne.Name.Sym, out var sym)
            && sym.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Enum
            && ctx.Types.GetByID(sym.Type, out var namedByName) && NamedTypeName(namedByName) != null)
        {
            gen.Write($"reflect.get({Quote(NamedId(namedByName))})");
            return true;
        }

        if (arg.Type != TypID.Invalid && ctx.Types.GetByID(arg.Type, out var t))
        {
            if (NamedTypeName(t) != null)
            {
                gen.Write($"reflect.get({Quote(NamedId(t))})");
                return true;
            }
            if (t.Kind == TypeKind.PrimitiveAny)
            {
                gen.Write("reflect.dynamic(");
                EmitExpr(ctx, pkg, gen, arg);
                gen.Write(")");
                return true;
            }
            gen.Write($"({TypeDesc(ctx, t)})");
            return true;
        }

        gen.Write("reflect.dynamic(");
        EmitExpr(ctx, pkg, gen, arg);
        gen.Write(")");
        return true;
    }

    private static string? NamedTypeName(Type t) => t switch
    {
        ClassType c => c.Name,
        InterfaceType i => i.Name,
        EnumType e => e.Name,
        _ => null
    };

    private static void WriteBlock(LuaGenerator gen, string? block)
    {
        if (string.IsNullOrEmpty(block)) return;
        gen.Write(block);
        gen.NewLine();
    }

    private static bool IsReflectable(PassContext ctx, List<Annotation> annotations) =>
        ctx.Config.Reflection.Mode switch
        {
            ReflectionMode.All => true,
            ReflectionMode.Annotated => annotations.Any(a => a.Name.Name == "reflectable"),
            _ => false
        };

    /// <summary>Id for a declaration defined in the file currently being emitted (function/variable).</summary>
    private string ReflectId(PassContext ctx, string name) => $"{_reflectNamespace}::{name}";

    /// <summary>Id for a named type, from its stored <see cref="Type.ReflectionId"/> (defining-package aware).</summary>
    private string NamedId(Type t) => t.ReflectionId ?? $"{_reflectNamespace}::{NamedTypeName(t) ?? "?"}";

    /// <summary>The reflection namespace for a package (root uses its config name, deps their dir leaf).</summary>
    private static string ReflectNamespace(PassContext ctx, PackageContext pkg)
    {
        var leaf = System.IO.Path.GetFileNameWithoutExtension(pkg.Path.TrimEnd('/', '\\'));
        var isRoot = ReferenceEquals(pkg, ctx.Pkgs.FirstOrDefault());
        return isRoot ? ctx.Config.Name ?? (string.IsNullOrEmpty(leaf) ? "main" : leaf)
                      : string.IsNullOrEmpty(leaf) ? "main" : leaf;
    }

    private string? ClassMeta(PassContext ctx, PackageContext pkg, ClassDecl cd)
    {
        if (!TryResolveType<ClassType>(ctx, pkg, cd.Name, out var ct)) return null;
        var runtime = ResolveName(ctx, pkg, cd.Name);
        var id = NamedId(ct);

        var parts = new List<string> { $"name = {Quote(ct.Name)}", "kind = \"class\"" };
        if (ct.IsAbstract) parts.Add("isAbstract = true");
        if (ct.BaseClass != null) parts.Add($"base = {Quote(NamedId(ct.BaseClass))}");
        if (ct.Interfaces.Count > 0)
            parts.Add($"interfaces = {{ {string.Join(", ", ct.Interfaces.Select(NamedId).Select(Quote))} }}");
        parts.Add($"fields = {{ {FieldsList(ctx, cd.Fields, ct)} }}");
        parts.Add($"methods = {{ {MethodsList(ctx, cd.Methods, ct)} }}");
        var anns = AnnotationsLiteral(cd.Annotations);
        if (anns != null) parts.Add($"annotations = {anns}");
        parts.Add($"ref = {runtime}");

        return $"__nebra[{Quote(id)}] = {{ {string.Join(", ", parts)} }};\n{runtime}.__nebra = {Quote(id)};";
    }

    private string? InterfaceMeta(PassContext ctx, PackageContext pkg, InterfaceDecl id)
    {
        if (!TryResolveType<InterfaceType>(ctx, pkg, id.Name, out var it)) return null;

        var parts = new List<string> { $"name = {Quote(it.Name)}", "kind = \"interface\"" };
        if (it.BaseInterfaces.Count > 0)
            parts.Add($"bases = {{ {string.Join(", ", it.BaseInterfaces.Select(NamedId).Select(Quote))} }}");
        var fields = it.Fields.Select(kv => $"{{ name = {Quote(kv.Key)}, type = {TypeDesc(ctx, kv.Value.Type)} }}");
        parts.Add($"fields = {{ {string.Join(", ", fields)} }}");
        var methods = it.Methods
            .Where(kv => !kv.Key.StartsWith("__", StringComparison.Ordinal))
            .Select(kv => MethodDesc(ctx, kv.Key, kv.Value, hasSelf: false, null, null));
        parts.Add($"methods = {{ {string.Join(", ", methods)} }}");
        var anns = AnnotationsLiteral(id.Annotations);
        if (anns != null) parts.Add($"annotations = {anns}");

        return $"__nebra[{Quote(NamedId(it))}] = {{ {string.Join(", ", parts)} }};";
    }

    private string? EnumMeta(PassContext ctx, PackageContext pkg, EnumDecl ed)
    {
        if (!TryResolveType<EnumType>(ctx, pkg, ed.Name, out var et)) return null;
        var runtime = ResolveName(ctx, pkg, ed.Name);
        var id = NamedId(et);

        var members = et.Members.Select(m => $"{{ name = {Quote(m.Name)}, value = {EnumValueLiteral(m.Value)} }}");
        var parts = new List<string>
        {
            $"name = {Quote(et.Name)}", "kind = \"enum\"",
            $"members = {{ {string.Join(", ", members)} }}"
        };
        var anns = AnnotationsLiteral(ed.Annotations);
        if (anns != null) parts.Add($"annotations = {anns}");
        parts.Add($"ref = {runtime}");

        return $"__nebra[{Quote(id)}] = {{ {string.Join(", ", parts)} }};\n{runtime}.__nebra = {Quote(id)};";
    }

    private string? FunctionMeta(PassContext ctx, PackageContext pkg, FunctionDecl fd)
    {
        var name = fd.NamePath[0];
        if (name.Sym == SymID.Invalid || !pkg.Syms.GetByID(name.Sym, out var sym)) return null;
        if (!ctx.Types.GetByID(sym.Type, out var t) || t is not FunctionType fn) return null;
        var runtime = ResolveName(ctx, pkg, name);

        var parts = new List<string>
        {
            $"name = {Quote(name.Name)}", "kind = \"function\"",
            ParamsAndReturn(ctx, fn, hasSelf: false)
        };
        var anns = AnnotationsLiteral(fd.Annotations);
        if (anns != null) parts.Add($"annotations = {anns}");
        parts.Add($"ref = {runtime}");

        return $"__nebra[{Quote(ReflectId(ctx, name.Name))}] = {{ {string.Join(", ", parts)} }};";
    }

    private string? VariableMeta(PassContext ctx, PackageContext pkg, AttribVar v)
    {
        if (v.Name.Sym == SymID.Invalid || !pkg.Syms.GetByID(v.Name.Sym, out var sym)) return null;
        if (sym.Type == TypID.Invalid || !ctx.Types.GetByID(sym.Type, out var t)) return null;
        var runtime = ResolveName(ctx, pkg, v.Name);

        return $"__nebra[{Quote(ReflectId(ctx, v.Name.Name))}] = {{ name = {Quote(v.Name.Name)}, kind = \"variable\", " +
               $"type = {TypeDesc(ctx, t)}, ref = function() return {runtime} end }};";
    }

    private string FieldsList(PassContext ctx, List<ClassFieldNode> fields, ClassType ct)
    {
        var items = new List<string>();
        foreach (var f in fields)
        {
            if (f.IsStatic || f.IsLocal) continue;
            if (!ct.InstanceFields.TryGetValue(f.Name.Name, out var field)) continue;
            var extra = new StringBuilder();
            if (f.DefaultValue != null) extra.Append(", hasDefault = true");
            if (f.IsProtected) extra.Append(", isProtected = true");
            var anns = AnnotationsLiteral(f.Annotations);
            if (anns != null) extra.Append($", annotations = {anns}");
            items.Add($"{{ name = {Quote(f.Name.Name)}, type = {TypeDesc(ctx, field.Type)}{extra} }}");
        }
        return string.Join(", ", items);
    }

    private string MethodsList(PassContext ctx, List<ClassMethodNode> methods, ClassType ct)
    {
        var items = new List<string>();
        foreach (var m in methods)
        {
            if (m.IsLocal) continue;
            var fn = ct.Methods.GetValueOrDefault(m.Name.Name) ?? ct.StaticMethods.GetValueOrDefault(m.Name.Name);
            if (fn == null) continue;
            var flags = new StringBuilder();
            if (m.IsStatic) flags.Append(", isStatic = true");
            if (m.IsAbstract) flags.Append(", isAbstract = true");
            if (m.IsOverride) flags.Append(", isOverride = true");
            if (m.IsAsync) flags.Append(", isAsync = true");
            items.Add(MethodDesc(ctx, m.Name.Name, fn, hasSelf: !m.IsStatic, flags.ToString(), AnnotationsLiteral(m.Annotations)));
        }
        return string.Join(", ", items);
    }

    private string MethodDesc(PassContext ctx, string name, FunctionType fn, bool hasSelf, string? flags, string? anns)
    {
        var annStr = anns != null ? $", annotations = {anns}" : "";
        return $"{{ name = {Quote(name)}, {ParamsAndReturn(ctx, fn, hasSelf)}{flags}{annStr} }}";
    }

    private string ParamsAndReturn(PassContext ctx, FunctionType fn, bool hasSelf)
    {
        var start = hasSelf && fn.ParamNames.Count > 0 && fn.ParamNames[0] == "self" ? 1 : 0;
        var ps = new List<string>();
        for (var i = start; i < fn.ParamTypes.Count; i++)
        {
            var pName = i < fn.ParamNames.Count ? fn.ParamNames[i] : $"arg{i}";
            ps.Add($"{{ name = {Quote(pName)}, type = {TypeDesc(ctx, fn.ParamTypes[i])} }}");
        }
        return $"params = {{ {string.Join(", ", ps)} }}, returns = {TypeDesc(ctx, fn.ReturnType)}";
    }

    /// <summary>Renders a type as a runtime <c>TypeDesc</c> table literal string.</summary>
    private string TypeDesc(PassContext ctx, Type t) => t switch
    {
        TableArrayType a => $"{{ kind = \"array\", element = {TypeDesc(ctx, a.ElementType)} }}",
        TableMapType m => $"{{ kind = \"map\", key = {TypeDesc(ctx, m.KeyType)}, value = {TypeDesc(ctx, m.ValueType)} }}",
        UnionType u => $"{{ kind = \"union\", types = {{ {string.Join(", ", u.Types.Select(x => TypeDesc(ctx, x)))} }} }}",
        VariadicType v => $"{{ kind = \"variadic\", element = {TypeDesc(ctx, v.ElementType)} }}",
        TupleType tup => $"{{ kind = \"tuple\", elements = {{ {string.Join(", ", tup.Fields.Select(f => TypeDesc(ctx, f.Type)))} }} }}",
        FunctionType fn => $"{{ kind = \"function\", params = {{ {string.Join(", ", fn.ParamTypes.Select(p => TypeDesc(ctx, p)))} }}, returns = {TypeDesc(ctx, fn.ReturnType)} }}",
        ClassType c => $"{{ kind = \"named\", id = {Quote(NamedId(c))} }}",
        InterfaceType i => $"{{ kind = \"named\", id = {Quote(NamedId(i))} }}",
        EnumType e => $"{{ kind = \"named\", id = {Quote(NamedId(e))} }}",
        _ => $"{{ kind = \"primitive\", name = {Quote(PrimitiveName(t.Kind))} }}"
    };

    private string? AnnotationsLiteral(List<Annotation> anns)
    {
        if (anns.Count == 0) return null;
        var items = anns.Select(a =>
        {
            var args = a.Args.Select(arg =>
                arg.Name != null ? $"{arg.Name} = {LiteralOf(arg.Value)}" : LiteralOf(arg.Value));
            return $"{{ name = {Quote(a.Name.Name)}, args = {{ {string.Join(", ", args)} }} }}";
        });
        return $"{{ {string.Join(", ", items)} }}";
    }

    /// <summary>Renders a constant annotation argument as a Lua literal (best-effort).</summary>
    private string LiteralOf(Expr e) => e switch
    {
        NumberLiteralExpr n => n.Raw,
        StringLiteralExpr s => Quote(s.Value),
        BoolLiteralExpr b => b.Value ? "true" : "false",
        NilLiteralExpr => "nil",
        UnaryExpr { Op: UnaryOp.Negate, Operand: NumberLiteralExpr n } => $"-{n.Raw}",
        _ => "nil"
    };

    private bool TryResolveType<T>(PassContext ctx, PackageContext pkg, NameRef name, out T type) where T : Type
    {
        type = null!;
        if (name.Sym == SymID.Invalid || !pkg.Syms.GetByID(name.Sym, out var sym)) return false;
        if (!ctx.Types.GetByID(sym.Type, out var t) || t is not T typed) return false;
        type = typed;
        return true;
    }

    private static string PrimitiveName(TypeKind kind) => kind switch
    {
        TypeKind.PrimitiveNil => "nil",
        TypeKind.PrimitiveNumber => "number",
        TypeKind.PrimitiveBool => "boolean",
        TypeKind.PrimitiveString => "string",
        TypeKind.PrimitiveFunction => "function",
        TypeKind.PrimitiveThread => "thread",
        TypeKind.PrimitiveUserdata => "userdata",
        TypeKind.PrimitiveNever => "never",
        _ => "any"
    };

    private static string EnumValueLiteral(object? value) => value switch
    {
        null => "nil",
        string s => Quote(s),
        _ => value.ToString() ?? "nil"
    };

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        sb.Append('"');
        return sb.ToString();
    }
}
