namespace Nebra.Runtime.Bindings;

/// <summary>
/// Base class for binding values that know how to push themselves onto a Lua state.
/// Concrete implementations are <see cref="NebraTable"/> and <see cref="NebraClass"/>.
/// </summary>
public abstract class NebraValue
{
    /// <summary>
    /// Pushes this value onto the runtime's Lua stack. The implementation must leave
    /// exactly one value on the stack.
    /// </summary>
    internal abstract void Push(NebraRuntime rt);
}
