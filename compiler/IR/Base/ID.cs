namespace Nebra.IR;

/// <summary>
/// Base class for all IR IDs. An IR ID is a unique identifier for an IR node, such as a function, a type, a variable, etc.
/// It is represented as a 64-bit unsigned integer. The actual value of the ID is not important, as long as it is unique within the IR.
/// The ID is used to reference IR nodes in various places, such as in the symbol table, in the type system, etc.
/// </summary>
public abstract class ID(ulong value) : IEquatable<ID>
{
    /// <summary>
    /// The value of an invalid ID. This is used to represent an ID that has not been assigned a valid value, such as when an error occurs during ID allocation or when an ID is not found in the symbol table.
    /// </summary>
    public const ulong InvalidValue = 0;
    
    /// <summary>
    /// The value of the ID.
    /// </summary>
    public ulong Value { get; } = value;

    public override string ToString()
    {
        return Value.ToString();
    }

    public bool Equals(ID? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ID)obj);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(ID? left, ID? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ID? left, ID? right)
    {
        return !Equals(left, right);
    }
    
    public static bool operator <(ID left, ID right)
    {
        return left.Value < right.Value;
    }

    public static bool operator >(ID left, ID right)
    {
        return left.Value > right.Value;
    }
    
    public static bool operator <=(ID left, ID right)
    {
        return left.Value <= right.Value;
    }
    
    public static bool operator >=(ID left, ID right)
    {
        return left.Value >= right.Value;
    }
    
    public static implicit operator ulong(ID id)
    {
        return id.Value;
    }

    private sealed class ValueEqualityComparer : IEqualityComparer<ID>
    {
        public bool Equals(ID? x, ID? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Value == y.Value;
        }

        public int GetHashCode(ID obj)
        {
            return obj.Value.GetHashCode();
        }
    }

    public static IEqualityComparer<ID> ValueComparer { get; } = new ValueEqualityComparer();
}

/// <summary>
/// A helper class for allocating unique IDs of a specific type. This class maintains a counter that is incremented each time a new ID is allocated, ensuring that each ID is unique.
/// </summary>
/// <typeparam name="T">The type of ID to allocate. This should be a subclass of <see cref="ID"/>.</typeparam>
public class IDAlloc<T> where T : ID
{
    private ulong _nextID = 1;

    /// <summary>
    /// Allocates a new ID of type <typeparamref name="T"/>. The ID is created by calling the constructor of <typeparamref name="T"/> with the next available ID value.
    /// </summary>
    /// <returns>A new ID of type <typeparamref name="T"/>.</returns>
    public T Next()
    {
        var id = _nextID++;
        return (T)Activator.CreateInstance(typeof(T), id)!;
    }
}

/// <summary>
/// Represents the ID of a node in the IR. This is a unique identifier that is assigned to each node in the IR and
/// can be used to reference the node from other nodes or from external code.
/// </summary>
public sealed class NodeID(ulong value) : ID(value)
{
    /// <summary>
    /// A special value that represents an invalid node ID. This is used to indicate that a node ID has not been assigned a valid value, such as when an error occurs during node creation or when a node ID is not found in the symbol table.
    /// </summary>
    public static readonly NodeID Invalid = new(InvalidValue);
    
    public static implicit operator NodeID(ulong value) => new(value);
}

/// <summary>
/// Represents the ID of a symbol in the IR. This is a unique identifier that is assigned to each symbol in the IR and
/// can be used to reference the symbol from other nodes or from external code.
/// </summary>
public sealed class SymID(ulong value) : ID(value)
{
    /// <summary>
    /// A special value that represents an invalid symbol ID. This is used to indicate that a symbol ID has not been assigned a valid value, such as when an error occurs during symbol creation or when a symbol ID is not found in the symbol table.
    /// </summary>
    public static readonly SymID Invalid = new(InvalidValue);
    
    public static implicit operator SymID(ulong value) => new(value);
}

/// <summary>
/// Represents the ID of a type in the IR. This is a unique identifier that is assigned to each type in the IR and
/// can be used to reference the type from other nodes or from external code.
/// </summary>
public sealed class TypID(ulong value) : ID(value)
{
    /// <summary>
    /// A special value that represents an invalid type ID. This is used to indicate that a type ID has not been assigned a valid value, such as when an error occurs during type creation or when a type ID is not found in the symbol table.
    /// </summary>
    public static readonly TypID Invalid = new(InvalidValue);
    
    public static implicit operator TypID(ulong value) => new(value);
}

/// <summary>
/// Represents the ID of a scope in the IR. This is a unique identifier that is assigned to each scope in the IR and
/// can be used to reference the scope from other nodes or from external code.
/// </summary>
public sealed class ScopeID(ulong value) : ID(value)
{
    /// <summary>
    /// A special value that represents an invalid scope ID. This is used to indicate that a scope ID has not been assigned a valid value, such as when an error occurs during scope creation or when a scope ID is not found in the symbol table.
    /// </summary>
    public static readonly ScopeID Invalid = new(InvalidValue);
    
    public static implicit operator ScopeID(ulong value) => new(value);
}