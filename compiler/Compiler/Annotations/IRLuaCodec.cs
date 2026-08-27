using System.Collections;
using System.Reflection;
using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.Compiler.Annotations;

/// <summary>
/// Reflection-based serialization of IR <see cref="Node"/> subtrees to and from
/// plain C# object trees (<see cref="Dictionary{TKey,TValue}"/> / <see cref="List{T}"/> /
/// primitives), which <see cref="NebraRuntime"/> can push into a Lua state as nested
/// tables. Used by the <c>ApplyAnnotationsPass</c> so annotation scripts can receive
/// the IR of their target declaration, transform it, and return a modified IR that
/// the pass splices back into the parent block.
///
/// Encoding strategy:
///   - Each <see cref="Node"/> becomes a <c>Dictionary&lt;string, object?&gt;</c> with a
///     <c>kind</c> field = concrete class name, plus one entry per public property.
///   - <see cref="NameRef"/>, <see cref="TextSpan"/>, enums, primitives, strings and
///     lists of the above are serialized directly.
///   - Types the codec does not know how to round-trip (e.g. <see cref="TypeRef"/>,
///     type parameters) are stored as opaque handles in an <see cref="_opaqueCache"/>
///     and replaced with a <c>{ __opaque = n }</c> marker so annotation scripts
///     preserve them untouched.
/// </summary>
public sealed class IRLuaCodec
{
    private const string KindField = "__kind";
    private const string IdField = "__id";
    private const string SpanField = "__span";
    private const string OpaqueField = "__opaque";

    /// <summary>
    /// Holds references to IR objects that the codec cannot round-trip through Lua.
    /// Annotation scripts see these as <c>{ __opaque = n }</c> tables and may return
    /// them unchanged to preserve the original C# reference.
    /// </summary>
    private readonly List<object> _opaqueCache = [];

    private static readonly HashSet<string> SkippedProperties = [
        nameof(Node.ID),
        nameof(Node.Span),
        "Type",
        "Sym",
        "Overloads",
        "ResolvedType",
    ];

    /// <summary>
    /// Class- and interface-member helpers that aren't <see cref="Node"/> subclasses
    /// but should still round-trip through annotation scripts so a class-level
    /// annotation can read its methods' annotations / parameters / names. Encoded
    /// the same way as Nodes (kind tag + reflection over public props) minus the
    /// node ID (these don't carry one). Decoding rebuilds them by reflecting over
    /// the constructor in the same way <see cref="DecodeNode"/> does.
    /// </summary>
    private static readonly HashSet<System.Type> HelperTypes = new()
    {
        typeof(ClassFieldNode),
        typeof(ClassMethodNode),
        typeof(ClassConstructorNode),
        typeof(ClassAccessorNode),
        typeof(InterfaceFieldNode),
        typeof(InterfaceMethodNode),
        typeof(EnumMember),
        typeof(AnnotationArg),
        typeof(AttribVar),
        typeof(ImportSpecifier),
        typeof(ElseIfClause),
    };

    public object? Encode(Node? node)
    {
        if (node == null) return null;
        return EncodeNode(node);
    }

    /// <summary>
    /// Encodes either a <see cref="Node"/> or a known helper-type (e.g.
    /// <see cref="ClassMethodNode"/>) into the codec's wire format. Used by
    /// <see cref="Passes.ApplyAnnotationsPass"/> when handing class/interface
    /// members to their annotation scripts — those helpers aren't <c>Node</c>s
    /// but the codec round-trips them all the same.
    /// </summary>
    public object? EncodeAny(object? value)
    {
        if (value == null) return null;
        if (value is Node node) return EncodeNode(node);
        if (HelperTypes.Contains(value.GetType())) return EncodeHelper(value);
        return null;
    }

    public Node Decode(object? value, IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        if (value is not Dictionary<string, object?> dict)
            throw new InvalidOperationException("Annotation apply returned a non-table value where a node was expected.");
        return DecodeNode(dict, alloc, fallbackSpan);
    }

    /// <summary>
    /// Decodes either a <see cref="Node"/> or a known helper-type into a
    /// concrete C# instance. <paramref name="expectedType"/> is used as the
    /// target for helper-type lookup; pass <c>typeof(ClassMethodNode)</c> when
    /// decoding a class method back from the annotation sandbox.
    /// </summary>
    public object? DecodeAny(object? value, System.Type expectedType, IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        if (value is not Dictionary<string, object?> dict)
            throw new InvalidOperationException("Annotation apply returned a non-table value where a node was expected.");
        if (HelperTypes.Contains(expectedType))
            return DecodeHelper(dict, expectedType, alloc, fallbackSpan);
        return DecodeNode(dict, alloc, fallbackSpan);
    }

    public List<Stmt> DecodeStmtList(object? value, IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        var result = new List<Stmt>();
        if (value is Dictionary<string, object?> singleDict)
        {
            var node = DecodeNode(singleDict, alloc, fallbackSpan);
            if (node is Stmt s) result.Add(s);
            else throw new InvalidOperationException($"Expected a statement node, got {node.GetType().Name}.");
            return result;
        }
        if (value is List<object?> list)
        {
            foreach (var item in list)
            {
                if (item is not Dictionary<string, object?> itemDict)
                    throw new InvalidOperationException("Annotation returned list contains a non-table element.");
                var node = DecodeNode(itemDict, alloc, fallbackSpan);
                if (node is Stmt s) result.Add(s);
                else throw new InvalidOperationException($"Expected a statement node, got {node.GetType().Name}.");
            }
            return result;
        }
        throw new InvalidOperationException("Annotation apply returned an unrecognized value.");
    }

    /// <summary>
    /// Checks whether the given encoded value is a raw list of nodes (as opposed to a single node dict).
    /// Used by the apply pass to distinguish "replace target with one node" from "splice this list in place of target".
    /// </summary>
    public static bool IsList(object? value) => value is List<object?>;

    #region Encoding

    private Dictionary<string, object?> EncodeNode(Node node)
    {
        var dict = new Dictionary<string, object?>
        {
            [KindField] = node.GetType().Name,
            [IdField] = (long)node.ID.Value,
            [SpanField] = EncodeSpan(node.Span),
        };

        foreach (var prop in GetSerializableProperties(node.GetType()))
        {
            var value = prop.GetValue(node);
            dict[CamelCase(prop.Name)] = EncodeValue(value);
        }

        return dict;
    }

    private object? EncodeValue(object? value)
    {
        if (value == null) return null;
        switch (value)
        {
            case string or bool or int or long or double or float:
                return value;
            case ulong ul:
                return (long)ul;
            case Enum e:
                return e.ToString();
            case NameRef nr:
                return new Dictionary<string, object?>
                {
                    ["name"] = nr.Name,
                    ["span"] = EncodeSpan(nr.Span),
                };
            case TextSpan ts:
                return EncodeSpan(ts);
            case Node n:
                return EncodeNode(n);
            case IList list:
                var result = new List<object?>(list.Count);
                foreach (var item in list) result.Add(EncodeValue(item));
                return result;
        }

        if (HelperTypes.Contains(value.GetType()))
            return EncodeHelper(value);

        return Opaque(value);
    }

    private Dictionary<string, object?> EncodeHelper(object helper)
    {
        var dict = new Dictionary<string, object?>
        {
            [KindField] = helper.GetType().Name,
        };

        var spanProp = helper.GetType().GetProperty(nameof(Node.Span));
        if (spanProp != null && spanProp.GetValue(helper) is TextSpan helperSpan)
            dict[SpanField] = EncodeSpan(helperSpan);

        foreach (var prop in GetSerializableProperties(helper.GetType()))
        {
            var value = prop.GetValue(helper);
            dict[CamelCase(prop.Name)] = EncodeValue(value);
        }

        return dict;
    }

    private Dictionary<string, object?> Opaque(object value)
    {
        var idx = _opaqueCache.Count;
        _opaqueCache.Add(value);
        return new Dictionary<string, object?> { [OpaqueField] = (long)idx };
    }

    private static List<object?> EncodeSpan(TextSpan span) => [
        (long)span.StartLn, (long)span.StartCol, (long)span.EndLn, (long)span.EndCol,
    ];

    #endregion

    #region Decoding

    private Node DecodeNode(Dictionary<string, object?> dict, IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        if (dict.TryGetValue(OpaqueField, out var opaqueIdxObj) && opaqueIdxObj is long opaqueIdx)
        {
            var cached = _opaqueCache[(int)opaqueIdx];
            if (cached is Node cachedNode) return cachedNode;
            throw new InvalidOperationException("Opaque handle does not refer to a Node.");
        }

        if (!dict.TryGetValue(KindField, out var kindObj) || kindObj is not string kind)
            throw new InvalidOperationException("Encoded node is missing a 'kind' field.");

        var type = ResolveType(kind) ?? throw new InvalidOperationException($"Unknown IR node type '{kind}'.");
        var ctor = PickConstructor(type);
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        var span = DecodeSpan(dict.GetValueOrDefault(SpanField)) ?? fallbackSpan;

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var name = p.Name ?? "";

            if (typeof(NodeID).IsAssignableFrom(p.ParameterType) && name.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                if (dict.TryGetValue(IdField, out var idObj) && idObj is long idVal and > 0)
                    args[i] = new NodeID((ulong)idVal);
                else
                    args[i] = alloc.Next();
                continue;
            }

            if (p.ParameterType == typeof(TextSpan) && name.Equals("span", StringComparison.OrdinalIgnoreCase))
            {
                args[i] = span;
                continue;
            }

            var key = CamelCase(name);
            if (!dict.TryGetValue(key, out var raw))
            {
                args[i] = DefaultFor(p);
                continue;
            }

            args[i] = DecodeValue(raw, p.ParameterType, alloc, span);
        }

        var node = (Node)ctor.Invoke(args);

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanWrite || SkippedProperties.Contains(prop.Name)) continue;
            var key = CamelCase(prop.Name);
            if (!dict.TryGetValue(key, out var raw)) continue;
            if (parameters.Any(p => CamelCase(p.Name ?? "") == key)) continue;
            try
            {
                prop.SetValue(node, DecodeValue(raw, prop.PropertyType, alloc, span));
            }
            catch
            {
                // Swallow — annotation scripts must not be able to crash compilation via a bad field.
            }
        }
        return node;
    }

    private object? DecodeValue(object? raw, System.Type target, IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        switch (raw)
        {
            case null:
                return null;
            case Dictionary<string, object?> objDict
                when objDict.TryGetValue(OpaqueField, out var opaqueIdxObj) && opaqueIdxObj is long opaqueIdx:
                return _opaqueCache[(int)opaqueIdx];
        }

        var nullable = Nullable.GetUnderlyingType(target);
        if (nullable != null) target = nullable;

        if (target == typeof(string))
            return raw is string s ? s : raw.ToString();
        if (target == typeof(bool))
            return raw is bool b ? b : Convert.ToBoolean(raw);
        if (target == typeof(int))
            return raw is long li ? (int)li : Convert.ToInt32(raw);
        if (target == typeof(long))
            return raw is long ll ? ll : Convert.ToInt64(raw);
        if (target == typeof(double))
            return raw is double dd ? dd : Convert.ToDouble(raw);
        if (target == typeof(float))
            return raw is double df ? (float)df : Convert.ToSingle(raw);

        if (target.IsEnum)
            return Enum.Parse(target, raw.ToString() ?? "", ignoreCase: true);

        if (target == typeof(TextSpan))
            return DecodeSpan(raw) ?? fallbackSpan;

        if (target == typeof(NameRef))
        {
            switch (raw)
            {
                case Dictionary<string, object?> nrDict:
                {
                    var nameStr = nrDict.GetValueOrDefault("name") as string ?? "";
                    var nrSpan = DecodeSpan(nrDict.GetValueOrDefault("span")) ?? fallbackSpan;
                    return new NameRef(nameStr, nrSpan);
                }
                case string plain:
                    return new NameRef(plain, fallbackSpan);
            }
        }

        if (typeof(Node).IsAssignableFrom(target))
        {
            if (raw is Dictionary<string, object?> d) return DecodeNode(d, alloc, fallbackSpan);
            return null;
        }

        if (HelperTypes.Contains(target))
        {
            if (raw is Dictionary<string, object?> hd) return DecodeHelper(hd, target, alloc, fallbackSpan);
            return null;
        }

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elemType = target.GetGenericArguments()[0];
            var listResult = (IList)Activator.CreateInstance(target)!;
            if (raw is List<object?> rawList)
            {
                foreach (var item in rawList)
                    listResult.Add(DecodeValue(item, elemType, alloc, fallbackSpan));
            }
            return listResult;
        }

        return raw;
    }

    private object DecodeHelper(Dictionary<string, object?> dict, System.Type target,
        IDAlloc<NodeID> alloc, TextSpan fallbackSpan)
    {
        if (dict.TryGetValue(OpaqueField, out var opaqueIdxObj) && opaqueIdxObj is long opaqueIdx)
        {
            var cached = _opaqueCache[(int)opaqueIdx];
            return cached;
        }

        var ctor = PickConstructor(target);
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        var span = DecodeSpan(dict.GetValueOrDefault(SpanField)) ?? fallbackSpan;

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var name = p.Name ?? "";

            if (p.ParameterType == typeof(TextSpan) && name.Equals("span", StringComparison.OrdinalIgnoreCase))
            {
                args[i] = span;
                continue;
            }

            var key = CamelCase(name);
            if (!dict.TryGetValue(key, out var raw))
            {
                args[i] = DefaultFor(p);
                continue;
            }

            args[i] = DecodeValue(raw, p.ParameterType, alloc, span);
        }

        var instance = ctor.Invoke(args);

        foreach (var prop in target.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanWrite || SkippedProperties.Contains(prop.Name)) continue;
            var key = CamelCase(prop.Name);
            if (!dict.TryGetValue(key, out var raw)) continue;
            if (parameters.Any(p => CamelCase(p.Name ?? "") == key)) continue;
            try
            {
                prop.SetValue(instance, DecodeValue(raw, prop.PropertyType, alloc, span));
            }
            catch
            {
                // swallow — same hardening as DecodeNode
            }
        }

        return instance;
    }

    private static TextSpan? DecodeSpan(object? raw)
    {
        if (raw is not List<object?> list || list.Count < 4) return null;
        return new TextSpan(
            ToInt(list[0]),
            ToInt(list[1]),
            ToInt(list[2]),
            ToInt(list[3])
        );

        static int ToInt(object? o) => o switch
        {
            long l => (int)l,
            int i => i,
            double d => (int)d,
            _ => 0,
        };
    }

    #endregion

    #region Reflection helpers

    private static readonly Dictionary<string, System.Type> TypeIndex = BuildTypeIndex();
    private static readonly Dictionary<System.Type, PropertyInfo[]> PropCache = new();

    private static Dictionary<string, System.Type> BuildTypeIndex()
    {
        var map = new Dictionary<string, System.Type>();
        var asm = typeof(Node).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (!typeof(Node).IsAssignableFrom(t)) continue;
            map[t.Name] = t;
        }
        return map;
    }

    private static System.Type? ResolveType(string kind) => TypeIndex.GetValueOrDefault(kind);

    private static ConstructorInfo PickConstructor(System.Type type)
    {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            throw new InvalidOperationException($"No public constructor on {type.Name}.");
        Array.Sort(ctors, (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));
        return ctors[0];
    }

    private static PropertyInfo[] GetSerializableProperties(System.Type type)
    {
        if (PropCache.TryGetValue(type, out var cached)) return cached;
        var props = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => !SkippedProperties.Contains(p.Name))
            .ToArray();
        PropCache[type] = props;
        return props;
    }

    private static string CamelCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (char.IsLower(s[0])) return s;
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    private static object? DefaultFor(ParameterInfo p)
    {
        if (p.HasDefaultValue) return p.DefaultValue;
        if (p.ParameterType.IsValueType) return Activator.CreateInstance(p.ParameterType);
        if (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(List<>))
            return Activator.CreateInstance(p.ParameterType);
        return null;
    }

    #endregion
}
