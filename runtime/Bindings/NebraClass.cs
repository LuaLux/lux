using System.Reflection;
using KeraLua;

namespace Nebra.Runtime.Bindings;

/// <summary>
/// Describes a class that should be visible from Lua. Owns a constructor (optional),
/// instance methods, static members, and an opaque metatable name. When pushed, the
/// class materialises as a Lua table whose <c>new</c> field constructs userdata
/// instances; the table is also callable (<c>MyClass(args)</c>) via a <c>__call</c>
/// metamethod. Instances are GCHandle-pinned userdata with the class's metatable
/// providing method dispatch through <c>__index</c>.
/// </summary>
public class NebraClass : NebraValue
{
    /// <summary>Name under which this class appears in Lua tables / globals.</summary>
    public string Name { get; }

    /// <summary>Unique identifier of the instance metatable in Lua's registry.</summary>
    public string MetaName { get; }

    /// <summary>The CLR type backing this class, or null for purely Lua-side classes.</summary>
    public Type? ClrType { get; }

    private CtorEntry? _constructor;
    private readonly Dictionary<string, MethodEntry> _instanceMembers = new();
    private readonly Dictionary<string, MethodEntry> _staticMembers = new();
    private readonly Dictionary<string, object?> _staticFields = new();

    private static int _autoMetaId;

    public NebraClass(string name, Type? clrType = null)
    {
        Name = name;
        ClrType = clrType;
        MetaName = clrType != null
            ? $"nebracls:{clrType.FullName}"
            : $"nebracls:{name}#{Interlocked.Increment(ref _autoMetaId)}";
    }

    /// <summary>Sets the constructor delegate. Its signature defines the Lua call shape.</summary>
    public NebraClass WithConstructor(Delegate ctor)
    {
        _constructor = CtorEntry.FromDelegate(ctor);
        return this;
    }

    /// <summary>
    /// Adds an instance method. The delegate's first parameter is the <c>self</c>
    /// instance and must be assignment-compatible with <see cref="ClrType"/>.
    /// </summary>
    public NebraClass WithMethod(string name, Delegate method)
    {
        _instanceMembers[name] = MethodEntry.FromInstanceDelegate(method);
        return this;
    }

    /// <summary>Adds a static (class-level) function callable as <c>MyClass.foo(...)</c>.</summary>
    public NebraClass WithStatic(string name, Delegate method)
    {
        _staticMembers[name] = MethodEntry.FromStandalone(method);
        return this;
    }

    /// <summary>Adds a static value (constant) accessible as <c>MyClass.field</c>.</summary>
    public NebraClass WithStaticField(string name, object? value)
    {
        _staticFields[name] = value;
        return this;
    }

    /// <summary>
    /// Builds a <see cref="NebraClass"/> by reflecting <typeparamref name="T"/>. If
    /// <typeparamref name="T"/> is annotated with <see cref="NebraExportAttribute"/>
    /// every public member is exposed; otherwise only those members carrying the
    /// attribute themselves are. The constructor is picked the same way (preferring
    /// an attributed ctor), and falls back to the parameterless ctor when present.
    /// </summary>
    public static NebraClass FromReflection<T>(string? name = null) where T : class
        => FromReflection(typeof(T), name);

    /// <summary>Non-generic variant of <see cref="FromReflection{T}"/>.</summary>
    public static NebraClass FromReflection(Type type, string? name = null)
    {
        var classAttr = type.GetCustomAttribute<NebraExportAttribute>();
        var implicitMode = classAttr != null;
        var displayName = name ?? classAttr?.Name ?? type.Name;
        var cls = new NebraClass(displayName, type);

        var ctor = PickConstructor(type, implicitMode);
        if (ctor != null)
            cls._constructor = CtorEntry.FromConstructor(ctor);

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.DeclaringType == typeof(object)) continue;
            if (m.IsSpecialName) continue;
            var memberAttr = m.GetCustomAttribute<NebraExportAttribute>();
            if (!implicitMode && memberAttr == null) continue;
            var alias = memberAttr?.Name ?? m.Name;
            cls._instanceMembers[alias] = MethodEntry.FromMethodInfo(m);
        }

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.IsSpecialName) continue;
            var memberAttr = m.GetCustomAttribute<NebraExportAttribute>();
            if (!implicitMode && memberAttr == null) continue;
            var alias = memberAttr?.Name ?? m.Name;
            cls._staticMembers[alias] = MethodEntry.FromMethodInfo(m);
        }

        return cls;
    }

    internal override void Push(NebraRuntime rt)
    {
        if (ClrType != null)
            rt.ClassRegistry[ClrType] = this;

        EnsureMetatable(rt);
        BuildClassTable(rt);
    }

    private void EnsureMetatable(NebraRuntime rt)
    {
        var state = rt.State;
        if (state.NewMetaTable(MetaName))
        {
            state.PushString("__index");
            state.NewTable();
            foreach (var (mname, entry) in _instanceMembers)
            {
                var fn = entry.WrapInstance(rt, this);
                rt.PinnedDelegates.Add(fn);
                state.PushCFunction(fn);
                state.SetField(-2, mname);
            }
            state.SetTable(-3);

            state.PushString("__gc");
            LuaFunction gc = NebraMarshal.CollectUserdata;
            rt.PinnedDelegates.Add(gc);
            state.PushCFunction(gc);
            state.SetTable(-3);
        }
        state.Pop(1);
    }

    private void BuildClassTable(NebraRuntime rt)
    {
        var state = rt.State;
        state.NewTable();

        foreach (var (sname, entry) in _staticMembers)
        {
            var fn = entry.WrapStandalone(rt);
            rt.PinnedDelegates.Add(fn);
            state.PushCFunction(fn);
            state.SetField(-2, sname);
        }

        foreach (var (fname, val) in _staticFields)
        {
            NebraMarshal.Push(rt, val);
            state.SetField(-2, fname);
        }

        if (_constructor != null)
        {
            var newFn = LuaFunctionWrapper.WrapConstructor(rt, this, _constructor, argStart: 1);
            rt.PinnedDelegates.Add(newFn);
            state.PushCFunction(newFn);
            state.SetField(-2, "new");

            state.NewTable();
            var callFn = LuaFunctionWrapper.WrapConstructor(rt, this, _constructor, argStart: 2);
            rt.PinnedDelegates.Add(callFn);
            state.PushCFunction(callFn);
            state.SetField(-2, "__call");
            state.SetMetaTable(-2);
        }
    }

    private static ConstructorInfo? PickConstructor(Type type, bool implicitMode)
    {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var attributed = ctors.FirstOrDefault(c => c.GetCustomAttribute<NebraExportAttribute>() != null);
        if (attributed != null) return attributed;
        if (!implicitMode) return null;
        return ctors
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
    }
}

/// <summary>
/// Storage for a constructor reachable either through reflection or as a
/// user-supplied delegate. Holds the parameter shape (used to read Lua args) and a
/// uniform invoker that returns the freshly-built CLR instance.
/// </summary>
internal sealed class CtorEntry
{
    public ParameterInfo[] Parameters { get; }
    public Func<object?[], object?> Invoker { get; }

    private CtorEntry(ParameterInfo[] pars, Func<object?[], object?> invoker)
    {
        Parameters = pars;
        Invoker = invoker;
    }

    public static CtorEntry FromConstructor(ConstructorInfo ci)
        => new(ci.GetParameters(), ci.Invoke);

    public static CtorEntry FromDelegate(Delegate del)
        => new(del.Method.GetParameters(), del.DynamicInvoke);
}

/// <summary>
/// Internal sum type that distinguishes the three ways a class member can be
/// supplied: a CLR <see cref="MethodInfo"/> (reflection path), a free-standing
/// delegate, or an instance-style delegate whose first parameter is <c>self</c>.
/// Each variant knows how to produce the corresponding Lua wrapper.
/// </summary>
internal sealed class MethodEntry
{
    private readonly MethodInfo? _method;
    private readonly Delegate? _delegate;
    private readonly Kind _kind;

    private enum Kind { MethodInfo, StandaloneDelegate, InstanceDelegate }

    private MethodEntry(Kind kind, MethodInfo? method, Delegate? del)
    {
        _kind = kind;
        _method = method;
        _delegate = del;
    }

    public static MethodEntry FromMethodInfo(MethodInfo m) => new(Kind.MethodInfo, m, null);
    public static MethodEntry FromStandalone(Delegate d) => new(Kind.StandaloneDelegate, null, d);
    public static MethodEntry FromInstanceDelegate(Delegate d) => new(Kind.InstanceDelegate, null, d);

    public LuaFunction WrapStandalone(NebraRuntime rt)
    {
        return _kind switch
        {
            Kind.StandaloneDelegate => LuaFunctionWrapper.WrapStandalone(rt, _delegate!),
            Kind.MethodInfo when _method!.IsStatic =>
                LuaFunctionWrapper.WrapStaticMethod(rt, _method),
            _ => throw new InvalidOperationException("Cannot wrap an instance member as standalone."),
        };
    }

    public LuaFunction WrapInstance(NebraRuntime rt, NebraClass owner)
    {
        return _kind switch
        {
            Kind.MethodInfo when !_method!.IsStatic =>
                LuaFunctionWrapper.WrapInstanceMethod(rt, owner, _method),
            Kind.InstanceDelegate => LuaFunctionWrapper.WrapInstanceDelegate(rt, owner, _delegate!),
            _ => throw new InvalidOperationException("Cannot wrap a static member as instance."),
        };
    }
}
