namespace Nebra.Runtime.Bindings;

/// <summary>
/// Builder for an arbitrary Lua table assembled from C# objects. Holds string-keyed
/// fields, an optional array part, and is itself a <see cref="NebraValue"/> so it can
/// be nested inside another <see cref="NebraTable"/> or used as a package root.
/// Insertion order is preserved for the string part.
/// </summary>
public sealed class NebraTable : NebraValue
{
    private readonly List<KeyValuePair<string, object?>> _fields = [];
    private readonly List<object?> _array = [];

    /// <summary>Optional name remembered when this table is added to a package /
    /// parent table by reference (e.g. <c>parent.SetTable(child)</c>).</summary>
    public string? Name { get; private set; }

    public NebraTable() { }

    public NebraTable(string name)
    {
        Name = name;
    }

    /// <summary>Sets a string-keyed field. Overwrites a prior value with the same key.</summary>
    public NebraTable Set(string key, object? value)
    {
        for (var i = 0; i < _fields.Count; i++)
        {
            if (_fields[i].Key != key) continue;
            _fields[i] = new KeyValuePair<string, object?>(key, value);
            return this;
        }
        _fields.Add(new KeyValuePair<string, object?>(key, value));
        return this;
    }

    /// <summary>Adds a function field — the delegate is wrapped on push.</summary>
    public NebraTable SetFunction(string name, Delegate fn) => Set(name, fn);

    /// <summary>Adds a nested table.</summary>
    public NebraTable SetTable(string name, NebraTable table) => Set(name, table);

    /// <summary>Adds a nested table and reuses the table's <see cref="Name"/> as the field key.</summary>
    public NebraTable SetTable(NebraTable table)
    {
        return table.Name == null ? throw new ArgumentException("NebraTable.SetTable(NebraTable): the supplied table has no Name; use the (name, table) overload.") : Set(table.Name, table);
    }

    /// <summary>Adds a class under the class's own <see cref="NebraClass.Name"/>.</summary>
    public NebraTable SetClass(NebraClass cls) => Set(cls.Name, cls);

    /// <summary>Appends a value to the 1-indexed array part of the table.</summary>
    public NebraTable Add(object? value)
    {
        _array.Add(value);
        return this;
    }

    internal override void Push(NebraRuntime rt)
    {
        var state = rt.State;
        state.NewTable();

        foreach (var kv in _fields)
        {
            NebraMarshal.Push(rt, kv.Value);
            state.SetField(-2, kv.Key);
        }

        for (var i = 0; i < _array.Count; i++)
        {
            NebraMarshal.Push(rt, _array[i]);
            state.RawSetInteger(-2, i + 1);
        }
    }
    
    public static NebraTable FromDictionary(IDictionary<string, object?> dict)
    {
        var table = new NebraTable();
        foreach (var kv in dict)
        {
            if (kv.Value is IDictionary<string, object?> nestedDict)
            {
                table.SetTable(kv.Key, FromDictionary(nestedDict));
            }
            else
            {
                table.Set(kv.Key, kv.Value);
            }
        }
        return table;
    }
}
