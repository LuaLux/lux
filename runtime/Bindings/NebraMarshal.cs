using System.Runtime.InteropServices;
using KeraLua;

namespace Nebra.Runtime.Bindings;

/// <summary>
/// Bidirectional value marshaller between C# and Lua. Handles primitives, strings,
/// nested dictionaries / lists, <see cref="NebraValue"/> trees (tables / classes),
/// CLR delegates (wrapped as Lua C functions) and CLR instances of types registered
/// through a <see cref="NebraClass"/> (pushed as userdata with the class's metatable).
/// </summary>
public static class NebraMarshal
{
    /// <summary>
    /// Pushes <paramref name="value"/> onto the runtime's Lua stack. For tables /
    /// classes the operation is recursive; for delegates a wrapping C function is
    /// pinned in <see cref="NebraRuntime.PinnedDelegates"/> so it survives GC.
    /// </summary>
    public static void Push(NebraRuntime rt, object? value)
    {
        var state = rt.State;
        switch (value)
        {
            case null:
                state.PushNil();
                return;
            case bool b:
                state.PushBoolean(b);
                return;
            case string s:
                state.PushString(s);
                return;
            case int i:
                state.PushInteger(i);
                return;
            case long l:
                state.PushInteger(l);
                return;
            case short sh:
                state.PushInteger(sh);
                return;
            case byte by:
                state.PushInteger(by);
                return;
            case sbyte sb:
                state.PushInteger(sb);
                return;
            case uint ui:
                state.PushInteger(ui);
                return;
            case ulong ul:
                state.PushInteger((long)ul);
                return;
            case double d:
                state.PushNumber(d);
                return;
            case float f:
                state.PushNumber(f);
                return;
            case decimal dec:
                state.PushNumber((double)dec);
                return;
            case char c:
                state.PushString(c.ToString());
                return;
            case NebraValue lv:
                lv.Push(rt);
                return;
            case Delegate del:
                {
                    var fn = LuaFunctionWrapper.WrapStandalone(rt, del);
                    rt.PinnedDelegates.Add(fn);
                    state.PushCFunction(fn);
                    return;
                }
            case IDictionary<string, object?> dict:
                state.NewTable();
                foreach (var kv in dict)
                {
                    Push(rt, kv.Value);
                    state.SetField(-2, kv.Key);
                }
                return;
            case System.Collections.IList list:
                state.NewTable();
                for (var i = 0; i < list.Count; i++)
                {
                    Push(rt, list[i]);
                    state.RawSetInteger(-2, i + 1);
                }
                return;
        }

        var clr = value.GetType();
        if (TryFindRegistered(rt, clr, out var cls))
        {
            PushUserdata(rt, value, cls!.MetaName);
            return;
        }

        throw new InvalidOperationException(
            $"NebraMarshal: cannot push CLR instance of type '{clr.FullName}' — no NebraClass is registered for it.");
    }

    /// <summary>
    /// Reads the Lua value at <paramref name="idx"/> and converts it to
    /// <paramref name="expected"/>. Pass <c>typeof(object)</c> for a generic read
    /// that returns primitives / Dictionary / List / pinned CLR instance directly.
    /// Throws when the conversion is not possible — call sites are expected to
    /// translate the exception into a Lua error via <see cref="Lua.Error()"/>.
    /// </summary>
    public static object? Read(NebraRuntime rt, Lua state, int idx, Type expected)
    {
        var t = state.Type(idx);
        if (t == LuaType.None || t == LuaType.Nil)
        {
            if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null)
                throw new ArgumentException($"argument {idx}: expected non-null {expected.Name}, got nil.");
            return null;
        }

        if (expected == typeof(object) || expected == typeof(void))
            return ReadAny(rt, state, idx);

        var underlying = Nullable.GetUnderlyingType(expected) ?? expected;

        if (underlying == typeof(bool)) return state.ToBoolean(idx);
        if (underlying == typeof(string)) return state.ToString(idx);
        if (underlying == typeof(int)) return (int)state.ToInteger(idx);
        if (underlying == typeof(long)) return state.ToInteger(idx);
        if (underlying == typeof(short)) return (short)state.ToInteger(idx);
        if (underlying == typeof(byte)) return (byte)state.ToInteger(idx);
        if (underlying == typeof(sbyte)) return (sbyte)state.ToInteger(idx);
        if (underlying == typeof(uint)) return (uint)state.ToInteger(idx);
        if (underlying == typeof(ulong)) return (ulong)state.ToInteger(idx);
        if (underlying == typeof(double)) return state.ToNumber(idx);
        if (underlying == typeof(float)) return (float)state.ToNumber(idx);
        if (underlying == typeof(decimal)) return (decimal)state.ToNumber(idx);
        if (underlying == typeof(char))
        {
            var s = state.ToString(idx);
            return string.IsNullOrEmpty(s) ? '\0' : s[0];
        }

        switch (t)
        {
            case LuaType.UserData:
            {
                var instance = ReadUserdata(state, idx);
                if (instance == null) return null;
                if (underlying.IsInstanceOfType(instance)) return instance;
                throw new ArgumentException(
                    $"argument {idx}: expected {underlying.Name}, got userdata of type {instance.GetType().Name}.");
            }
            case LuaType.Table:
                return ReadTable(rt, state, idx);
            default:
                return ReadAny(rt, state, idx);
        }
    }

    /// <summary>
    /// Generic Lua → C# read used when no specific target type is available.
    /// Tables become <see cref="List{T}"/> when keyed as a 1..n sequence and
    /// <see cref="Dictionary{TKey,TValue}"/> otherwise. Userdata is unwrapped to
    /// the original CLR instance. Functions / threads / light userdata are not
    /// representable and yield <c>null</c>.
    /// </summary>
    public static object? ReadAny(NebraRuntime rt, Lua state, int idx)
    {
        return state.Type(idx) switch
        {
            LuaType.Nil => null,
            LuaType.Boolean => state.ToBoolean(idx),
            LuaType.Number => state.IsInteger(idx) ? state.ToInteger(idx) : state.ToNumber(idx),
            LuaType.String => state.ToString(idx),
            LuaType.Table => ReadTable(rt, state, idx),
            LuaType.UserData => ReadUserdata(state, idx),
            _ => null,
        };
    }

    private static object ReadTable(NebraRuntime rt, Lua state, int idx)
    {
        var abs = state.AbsIndex(idx);
        var hasStringKey = false;
        var maxIntKey = 0;
        var intKeyCount = 0;

        state.PushNil();
        while (state.Next(abs))
        {
            var keyType = state.Type(-2);
            switch (keyType)
            {
                case LuaType.String:
                    hasStringKey = true;
                    break;
                case LuaType.Number when state.IsInteger(-2):
                {
                    var k = (int)state.ToInteger(-2);
                    if (k > maxIntKey) maxIntKey = k;
                    intKeyCount++;
                    break;
                }
            }
            state.Pop(1);
        }

        if (!hasStringKey && intKeyCount > 0 && maxIntKey == intKeyCount)
        {
            var list = new List<object?>(intKeyCount);
            for (var i = 1; i <= intKeyCount; i++)
            {
                state.RawGetInteger(abs, i);
                list.Add(ReadAny(rt, state, -1));
                state.Pop(1);
            }
            return list;
        }

        var dict = new Dictionary<string, object?>();
        state.PushNil();
        while (state.Next(abs))
        {
            var keyType = state.Type(-2);
            switch (keyType)
            {
                case LuaType.String:
                {
                    var key = state.ToString(-2) ?? "";
                    dict[key] = ReadAny(rt, state, -1);
                    break;
                }
                case LuaType.Number when state.IsInteger(-2):
                    dict[state.ToInteger(-2).ToString()] = ReadAny(rt, state, -1);
                    break;
            }
            state.Pop(1);
        }
        return dict;
    }

    /// <summary>
    /// Wraps a CLR instance in a Lua userdata block holding a pinned
    /// <see cref="GCHandle"/>, then assigns the named metatable from the registry.
    /// The handle is freed by the metatable's <c>__gc</c> hook installed by
    /// <see cref="NebraClass"/>.
    /// </summary>
    internal static void PushUserdata(NebraRuntime rt, object instance, string metaName)
    {
        var state = rt.State;
        var handle = GCHandle.Alloc(instance);
        var ud = state.NewUserData(IntPtr.Size);
        Marshal.WriteIntPtr(ud, GCHandle.ToIntPtr(handle));

        var mtType = state.GetMetaTable(metaName);
        if (mtType == LuaType.Nil)
        {
            handle.Free();
            state.Pop(2);
            throw new InvalidOperationException(
                $"NebraMarshal: metatable '{metaName}' is not registered. Push the NebraClass before pushing instances of it.");
        }
        state.SetMetaTable(-2);
    }

    /// <summary>
    /// Reads the CLR instance backing a Lua userdata at <paramref name="idx"/>.
    /// Returns <c>null</c> if the userdata is empty or has already been collected.
    /// </summary>
    internal static object? ReadUserdata(Lua state, int idx)
    {
        var ud = state.ToUserData(idx);
        if (ud == IntPtr.Zero) return null;
        var handlePtr = Marshal.ReadIntPtr(ud);
        if (handlePtr == IntPtr.Zero) return null;
        var handle = GCHandle.FromIntPtr(handlePtr);
        return handle.IsAllocated ? handle.Target : null;
    }

    /// <summary>
    /// __gc handler shared by every <see cref="NebraClass"/> metatable. Frees the
    /// pinned <see cref="GCHandle"/> stored in the userdata so the CLR instance
    /// becomes collectible again.
    /// </summary>
    internal static int CollectUserdata(IntPtr luaPtr)
    {
        var state = Lua.FromIntPtr(luaPtr);
        var ud = state.ToUserData(1);
        if (ud == IntPtr.Zero) return 0;
        var handlePtr = Marshal.ReadIntPtr(ud);
        if (handlePtr == IntPtr.Zero) return 0;
        var handle = GCHandle.FromIntPtr(handlePtr);
        if (handle.IsAllocated) handle.Free();
        Marshal.WriteIntPtr(ud, IntPtr.Zero);
        return 0;
    }

    private static bool TryFindRegistered(NebraRuntime rt, Type t, out NebraClass? cls)
    {
        if (rt.ClassRegistry.TryGetValue(t, out cls)) return true;
        for (var bt = t.BaseType; bt != null; bt = bt.BaseType)
        {
            if (rt.ClassRegistry.TryGetValue(bt, out cls)) return true;
        }
        foreach (var iface in t.GetInterfaces())
        {
            if (rt.ClassRegistry.TryGetValue(iface, out cls)) return true;
        }
        cls = null;
        return false;
    }
}
