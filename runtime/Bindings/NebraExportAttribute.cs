namespace Nebra.Runtime.Bindings;

/// <summary>
/// Marks a CLR type, constructor, or method as exportable to Lua via
/// <see cref="NebraClass.FromReflection{T}"/>. Applying it to a class enables
/// implicit export of every public member, otherwise only members that themselves
/// carry the attribute are exposed. The optional <see cref="Name"/> overrides the
/// Lua-visible name (defaults to the CLR member name).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Method)]
public sealed class NebraExportAttribute(string? name = null) : Attribute
{
    /// <summary>Optional alias under which the member appears in Lua.</summary>
    public string? Name { get; } = name;
}
