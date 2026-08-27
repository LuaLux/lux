using Nebra.IR;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The mangle pass is responsible for mangling the names of variables, functions, and other symbols in the source code. 
/// </summary>
public sealed class ManglePase() : Pass(PassName, PassScope.PerFile, true)
{
    public const string PassName = "Mangle";

    public override bool Run(PassContext context)
    {
        if (context.Pkg == null || context.File == null)
        {
            return false;
        }

        var mc = context.Config.Mangle;
        var shouldMangle = mc.Enabled || context.Config.Minify;

        foreach (var (id, sym) in context.Pkg.Syms.ByID)
        {
            if (sym.DeclaringNode == NodeID.Invalid)
            {
                context.Names.Add(id, sym.Name, sym.Name);
                continue;
            }

            if (!shouldMangle || !ShouldMangleSymbol(context, mc, sym))
            {
                context.Names.Add(id, sym.Name, sym.Name);
                continue;
            }

            var mangledName = sym.Kind == SymbolKind.Function
                ? MangleFunctionName(context, sym)
                : context.Names.RandName(GetManglePrefix(sym.Kind));

            context.Names.Add(id, sym.Name, mangledName);
        }
        
        return true;
    }

    private bool ShouldMangleSymbol(PassContext pc, Configuration.MangleSection mc, Symbol sym)
    {
        // `self` is bound by Lua itself: a method emitted as `function Cls:name(...)` receives it
        // implicitly, and an extension method takes it under that exact name. Renaming the symbol
        // would leave the body reading a global that nothing ever assigns.
        if (sym.Name == "self") return false;

        var isTopLevel = sym.Owner == pc.Pkg!.Root;

        if (sym.Kind == SymbolKind.Function)
        {
            if (mc.KeepFunctionNames) return false;
            if (isTopLevel && !mc.MangleTopLevel) return false;
            return true;
        }

        if (isTopLevel)
            return mc.MangleTopLevel;

        return mc.MangleLocals;
    }

    private string GetManglePrefix(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Variable => "v",
            SymbolKind.Function => "f",
            SymbolKind.Class => "cls",
            SymbolKind.Interface => "ifc",
            _ => "unk"
        };
    }

    private string MangleFunctionName(PassContext pc, Symbol sym)
    {
        if (!pc.Pkg!.Types.GetByID(sym.Type, out var typ) || typ is not FunctionType funcType)
        {
            return pc.Names.RandName("fn");
        }

        var i = 0;
        var sigSuffix = "";
        foreach (var param in funcType.ParamTypes)
        {
            if (i > 0)
            {
                sigSuffix += "_";
            }

            sigSuffix += GetTypeName(pc, param);
            i++;
        }
        
        var baseName = pc.Names.RandName("fn");
        if (sigSuffix.Length > 0)
        {
            baseName += "_" + sigSuffix;
        }
        return baseName;
    }

    private string GetTypeName(PassContext pc, TypID typID)
    {
        if (!pc.Pkg!.Types.GetByID(typID, out var typ))
        {
            return "unk";
        }

        switch (typ.Kind)
        {
            case TypeKind.PrimitiveAny:
                return "a";
            case TypeKind.PrimitiveNil:
                return "n";
            case TypeKind.PrimitiveString:
                return "s";
            case TypeKind.PrimitiveNumber:
                return "f";
            case TypeKind.PrimitiveBool:
                return "b";
            case TypeKind.TableArray:
                if (typ is not TableArrayType tableArrayType)
                {
                    return "unk";
                }
                return "arr" + GetTypeName(pc, tableArrayType.ElementType);
            case TypeKind.TableMap:
                if (typ is not TableMapType tableMapType)
                {
                    return "unk";
                }
                return "map" + GetTypeName(pc, tableMapType.KeyType) + GetTypeName(pc, tableMapType.ValueType);
            case TypeKind.Function:
                return "fn";
            case TypeKind.Tuple:
                return "tup";
            case TypeKind.Struct:
                return "st";
            case TypeKind.Enum:
                return "en";
            case TypeKind.Union:
                if (typ is not UnionType unionType)
                {
                    return "unk";
                }
                return "u" + string.Join("", unionType.Types.Select(t => GetTypeName(pc, t)));
            case TypeKind.Class:
                return "cls";
            case TypeKind.Interface:
                return "ifc";
            default:
                return "unk";
        }
    }
}