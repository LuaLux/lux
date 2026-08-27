using KeraLua;

namespace Nebra.Runtime.Bindings;

/// <summary>
/// Extension surface for <see cref="NebraRuntime"/> that hides KeraLua's stack
/// gymnastics behind a small object-oriented API. Lets callers register globals,
/// expose CLR classes, and assemble Lua-side packages without touching push/pop
/// directly.
/// </summary>
public static class NebraBindings
{
    /// <summary>
    /// Sets a Lua global to the given value. Strings, numbers, bools, lists,
    /// dictionaries, <see cref="NebraTable"/>, <see cref="NebraClass"/>, delegates and
    /// instances of registered CLR types are all accepted.
    /// </summary>
    public static void SetGlobal(this NebraRuntime rt, string name, object? value)
    {
        NebraMarshal.Push(rt, value);
        rt.State.SetGlobal(name);
    }

    /// <summary>
    /// Reads a Lua global as a generic C# object tree (primitives /
    /// <see cref="Dictionary{TKey,TValue}"/> / <see cref="List{T}"/> / unwrapped
    /// CLR instances). Returns <c>null</c> when the global is nil.
    /// </summary>
    public static object? GetGlobal(this NebraRuntime rt, string name)
    {
        var state = rt.State;
        var top = state.GetTop();
        try
        {
            state.GetGlobal(name);
            return NebraMarshal.ReadAny(rt, state, -1);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>
    /// Registers a class under the given Lua global name. Shorthand for
    /// <c>SetGlobal(name, cls)</c>.
    /// </summary>
    public static void RegisterClass(this NebraRuntime rt, NebraClass cls)
        => rt.SetGlobal(cls.Name, cls);

    /// <summary>
    /// Registers a class's metatable with the runtime without exposing it as a
    /// Lua global. Use for types that should only appear as return values of
    /// other functions (e.g. Spinner, ProgressBar) — their instance methods
    /// remain dispatchable while the class itself stays hidden.
    /// </summary>
    public static void RegisterClassHidden(this NebraRuntime rt, NebraClass cls)
    {
        cls.Push(rt);
        rt.State.Pop(1);
    }

    /// <summary>
    /// Builds a package — a Lua table holding the supplied classes — and assigns
    /// it to <paramref name="name"/>. The name may be dotted (<c>"foo.bar"</c>) to
    /// nest the package inside intermediate tables, which are auto-created when
    /// missing. Each class is exposed under its own <see cref="NebraClass.Name"/>.
    /// </summary>
    public static void CreatePackage(this NebraRuntime rt, string name, params NebraClass[] classes)
    {
        var pkg = new NebraTable();
        foreach (var c in classes) pkg.SetClass(c);
        rt.SetNested(name, pkg);
    }

    /// <summary>
    /// Variant of <see cref="CreatePackage(NebraRuntime, string, NebraClass[])"/> that
    /// accepts an arbitrary <see cref="NebraTable"/> as the package contents.
    /// </summary>
    public static void CreatePackage(this NebraRuntime rt, string name, NebraTable contents)
        => rt.SetNested(name, contents);

    /// <summary>
    /// Like <see cref="SetGlobal"/> but accepts a dotted path. Intermediate tables
    /// are created on demand; existing intermediate values that are not tables
    /// trigger an exception so we never silently overwrite unrelated globals.
    /// </summary>
    public static void SetNested(this NebraRuntime rt, string dottedName, object? value)
    {
        var parts = dottedName.Split('.');
        if (parts.Length == 1)
        {
            rt.SetGlobal(dottedName, value);
            return;
        }

        var state = rt.State;
        var top = state.GetTop();
        try
        {
            var rootType = state.GetGlobal(parts[0]);
            if (rootType == LuaType.Nil)
            {
                state.Pop(1);
                state.NewTable();
                state.PushCopy(-1);
                state.SetGlobal(parts[0]);
            }
            else if (rootType != LuaType.Table)
            {
                throw new InvalidOperationException(
                    $"SetNested: global '{parts[0]}' is not a table (it is {rootType}).");
            }

            for (var i = 1; i < parts.Length - 1; i++)
            {
                var childType = state.GetField(-1, parts[i]);
                if (childType == LuaType.Nil)
                {
                    state.Pop(1);
                    state.NewTable();
                    state.PushCopy(-1);
                    state.SetField(-3, parts[i]);
                }
                else if (childType != LuaType.Table)
                {
                    throw new InvalidOperationException(
                        $"SetNested: nested field '{parts[i]}' along path '{dottedName}' is not a table.");
                }
                state.Remove(-2);
            }

            NebraMarshal.Push(rt, value);
            state.SetField(-2, parts[^1]);
        }
        finally
        {
            state.SetTop(top);
        }
    }
}
