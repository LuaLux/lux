using System.Reflection;
using KeraLua;

namespace Nebra.Runtime.Bindings;

/// <summary>
/// Builds <see cref="LuaFunction"/> closures that adapt CLR methods to Lua's calling
/// convention. Each wrapper reads its arguments off the Lua stack via
/// <see cref="NebraMarshal.Read"/>, invokes the target via reflection, and pushes the
/// return value back. Exceptions are translated to Lua errors so they propagate
/// through <c>pcall</c> instead of crashing the host.
/// </summary>
internal static class LuaFunctionWrapper
{
    /// <summary>
    /// Wraps a free-standing delegate (used for global functions, static class
    /// members, and table function fields). All Lua arguments map 1:1 onto the
    /// delegate's parameters starting at stack index 1.
    /// </summary>
    public static LuaFunction WrapStandalone(NebraRuntime rt, Delegate del)
    {
        var method = del.Method;
        var target = del.Target;
        var pars = method.GetParameters();

        return luaPtr =>
        {
            var L = Lua.FromIntPtr(luaPtr);
            try
            {
                var args = ReadArgs(rt, L, pars, 1);
                var result = method.Invoke(target, args);
                return PushResult(rt, method.ReturnType, result);
            }
            catch (Exception ex)
            {
                return RaiseLuaError(L, ex);
            }
        };
    }

    /// <summary>
    /// Wraps an instance method bound through reflection (a <see cref="MethodInfo"/>
    /// on the owning CLR type). Lua call site is <c>obj:method(args)</c>; arg 1 is
    /// the userdata <c>self</c>, args 2..N+1 map onto the method's parameters.
    /// </summary>
    public static LuaFunction WrapInstanceMethod(NebraRuntime rt, NebraClass owner, MethodInfo method)
    {
        var pars = method.GetParameters();
        var typeName = owner.ClrType?.Name ?? owner.Name;

        return luaPtr =>
        {
            var L = Lua.FromIntPtr(luaPtr);
            try
            {
                var instance = NebraMarshal.ReadUserdata(L, 1)
                    ?? throw new ArgumentException(
                        $"{typeName}:{method.Name}: 'self' is missing or has been collected.");

                var args = ReadArgs(rt, L, pars, 2);
                var result = method.Invoke(instance, args);
                return PushResult(rt, method.ReturnType, result);
            }
            catch (Exception ex)
            {
                return RaiseLuaError(L, ex);
            }
        };
    }

    /// <summary>
    /// Wraps an instance method supplied as a delegate where the first parameter is
    /// the <c>self</c> instance (e.g. <c>Func&lt;T, int, string&gt;</c>). Reads
    /// <c>self</c> from arg 1, the remaining params from arg 2..N+1.
    /// </summary>
    public static LuaFunction WrapInstanceDelegate(NebraRuntime rt, NebraClass owner, Delegate del)
    {
        var method = del.Method;
        var target = del.Target;
        var pars = method.GetParameters();
        if (pars.Length == 0)
            throw new ArgumentException(
                $"Instance delegate for class '{owner.Name}' must take 'self' as its first parameter.");

        var typeName = owner.ClrType?.Name ?? owner.Name;

        return luaPtr =>
        {
            var L = Lua.FromIntPtr(luaPtr);
            try
            {
                var self = NebraMarshal.ReadUserdata(L, 1)
                    ?? throw new ArgumentException(
                        $"{typeName}:{method.Name}: 'self' is missing or has been collected.");

                var args = new object?[pars.Length];
                args[0] = self;
                for (var i = 1; i < pars.Length; i++)
                    args[i] = NebraMarshal.Read(rt, L, i + 1, pars[i].ParameterType);

                var result = method.Invoke(target, args);
                return PushResult(rt, method.ReturnType, result);
            }
            catch (Exception ex)
            {
                return RaiseLuaError(L, ex);
            }
        };
    }

    /// <summary>
    /// Wraps the <c>new</c> constructor entry of a class. Reads ctor args starting
    /// at <paramref name="argStart"/> (1 for <c>Class.new(...)</c>, 2 for
    /// <c>Class(...)</c> via the <c>__call</c> metamethod which receives the class
    /// table as arg 1).
    /// </summary>
    public static LuaFunction WrapConstructor(NebraRuntime rt, NebraClass owner, CtorEntry ctor, int argStart)
    {
        var pars = ctor.Parameters;

        return luaPtr =>
        {
            var L = Lua.FromIntPtr(luaPtr);
            try
            {
                var args = ReadArgs(rt, L, pars, argStart);
                var instance = ctor.Invoker(args);
                if (instance == null)
                {
                    L.PushNil();
                    return 1;
                }
                NebraMarshal.PushUserdata(rt, instance, owner.MetaName);
                return 1;
            }
            catch (Exception ex)
            {
                return RaiseLuaError(L, ex);
            }
        };
    }

    /// <summary>
    /// Wraps a static <see cref="MethodInfo"/> reflected from a CLR class. Behaves
    /// the same as <see cref="WrapStandalone"/> but invokes the method directly
    /// instead of going through a pre-built delegate.
    /// </summary>
    public static LuaFunction WrapStaticMethod(NebraRuntime rt, MethodInfo method)
    {
        var pars = method.GetParameters();

        return luaPtr =>
        {
            var L = Lua.FromIntPtr(luaPtr);
            try
            {
                var args = ReadArgs(rt, L, pars, 1);
                var result = method.Invoke(null, args);
                return PushResult(rt, method.ReturnType, result);
            }
            catch (Exception ex)
            {
                return RaiseLuaError(L, ex);
            }
        };
    }

    private static object?[] ReadArgs(NebraRuntime rt, Lua L, ParameterInfo[] pars, int argStart)
    {
        var args = new object?[pars.Length];
        for (var i = 0; i < pars.Length; i++)
        {
            var luaIdx = argStart + i;
            if (L.Type(luaIdx) == LuaType.None && pars[i].HasDefaultValue)
            {
                args[i] = pars[i].DefaultValue;
                continue;
            }
            args[i] = NebraMarshal.Read(rt, L, luaIdx, pars[i].ParameterType);
        }
        return args;
    }

    private static int PushResult(NebraRuntime rt, Type returnType, object? result)
    {
        if (returnType == typeof(void)) return 0;
        NebraMarshal.Push(rt, result);
        return 1;
    }

    private static int RaiseLuaError(Lua L, Exception ex)
    {
        var unwrapped = ex is TargetInvocationException { InnerException: not null } tex
            ? tex.InnerException
            : ex;
        L.PushString(unwrapped.Message);
        return L.Error();
    }
}
