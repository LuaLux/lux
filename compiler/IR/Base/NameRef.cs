using Nebra.Diagnostics;

namespace Nebra.IR;

/// <summary>
/// The name reference. This is used to refer to a name in the IR, such as a variable, a function, a type, etc. It can
/// be used to refer to a name that is defined in the same module or in another module. It can also be used to refer to
/// a name that is defined in the same scope or in an outer scope. The name reference can be resolved to the actual name
/// definition during name resolution.
/// </summary>
public sealed class NameRef(string name, TextSpan span, SymID? sym = null)
{
    /// <summary>
    /// The name of the name reference. This is the actual name that is being referred to, such as "x", "foo", "Bar", etc.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// The text span of the name reference. This indicates where in the source code the name reference corresponds to, and can be used for error reporting and other diagnostics.
    /// </summary>
    public TextSpan Span { get; } = span;

    /// <summary>
    /// The symbol ID of the name reference. This is assigned during name resolution, and can be used to reference the actual name definition from other nodes or from external code.
    /// If the name reference cannot be resolved, this will be set to SymID.Invalid.
    /// </summary>
    public SymID Sym { get; set; } = sym ?? SymID.Invalid;

    /// <summary>
    /// All overload candidates for this name reference. Populated when the name refers to
    /// an overloaded function (multiple symbols with the same name in the same scope).
    /// </summary>
    public List<SymID>? Overloads { get; set; }

    /// <summary>
    /// An implicit conversion operator that allows a NameRef to be implicitly converted to its SymID.
    /// </summary>
    public static implicit operator SymID(NameRef nameRef) => nameRef.Sym;
}

/// <summary>
/// The name map is a data structure that maps symbol IDs to their original names and to their mangled names. This is
/// used to keep track of the names of symbols in the IR, and to ensure that the mangled names are unique and do not
/// conflict with each other. 
/// </summary>
public sealed class NameMap
{
    private const string ValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int NormalLength = 8;
    
    private readonly Random _random = new();
    
    private readonly Dictionary<SymID, string> _toMangled = new();
    private readonly Dictionary<SymID, string> _toOriginal = new();
    private readonly Dictionary<string, bool> _usedNames = new();
    
    /// <summary>
    /// Adds a symbol with the specified ID, original name, and mangled name to the name map.
    /// </summary>
    /// <param name="sym">The symbol ID of the symbol to be added. This is used to reference the symbol from other nodes or from external code.</param>
    /// <param name="originalName">The original name of the symbol. This is the actual name that is being referred to, such as "x", "foo", "Bar", etc.</param>
    /// <param name="mangledName">The mangled name of the symbol. This is a unique name that is generated for the symbol to avoid conflicts with other symbols.</param>
    public void Add(SymID sym, string originalName, string mangledName)
    {
        _toOriginal[sym] = originalName;
        _toMangled[sym] = mangledName;
        _usedNames[mangledName] = true;
    }
    
    /// <summary>
    /// Tries to get the original name of the symbol with the specified ID. If the symbol does not have an original name, this method returns false and sets the output parameter to an empty string.
    /// </summary>
    /// <param name="sym">The symbol ID of the symbol whose original name is to be retrieved. This is used to reference the symbol from other nodes or from external code.</param>
    /// <param name="mangledName">When this method returns, contains the original name of the symbol with the specified ID if it exists in the name map; otherwise, an empty string.</param>
    /// <returns>true if the symbol with the specified ID has an original name in the name map; otherwise, false.</returns>
    public bool GetMangled(SymID sym, out string mangledName)
    {
        mangledName = "";
        
        if (_toMangled.TryGetValue(sym, out var outMangled))
        {
            mangledName = outMangled;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Tries to get the original name of the symbol with the specified ID. If the symbol does not have an original name, this method returns false and sets the output parameter to an empty string.
    /// </summary>
    /// <param name="sym">The symbol ID of the symbol whose original name is to be retrieved. This is used to reference the symbol from other nodes or from external code.</param>
    /// <param name="originalName">When this method returns, contains the original name of the symbol with the specified ID if it exists in the name map; otherwise, an empty string.</param>
    /// <returns>true if the symbol with the specified ID has an original name in the name map; otherwise, false.</returns>
    public bool GetOriginal(SymID sym, out string originalName)
    {
        originalName = "";
        
        if (_toOriginal.TryGetValue(sym, out var outOriginal))
        {
            originalName = outOriginal;
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Replaces the mangled name of the symbol with the specified ID with a new mangled name. If the symbol does not
    /// have a mangled name, this method adds a new entry to the name map with the specified ID and new mangled name.
    /// If the symbol already has a mangled name, this method updates the existing entry in the name map with the new
    /// mangled name and removes the old mangled name from the set of used names to allow it to be reused for other
    /// symbols in the future.
    /// </summary>
    /// <param name="sym">The symbol ID of the symbol whose mangled name is to be replaced. This is used to reference the symbol from other nodes or from external code.</param>
    /// <param name="newName">The new mangled name to be assigned to the symbol. This is a unique name that is generated for the symbol to avoid conflicts with other symbols.</param>
    public void ReplaceMangledName(SymID sym, string newName)
    {
        if (_toMangled.TryGetValue(sym, out var oldName))
        {
            _usedNames.Remove(oldName);
        }
        
        _toMangled[sym] = newName;
        _usedNames[newName] = true;
    }
    
    /// <summary>
    /// Gets the mangled name of the symbol with the specified ID. If the symbol does not have a mangled name, a new mangled name is generated and assigned to the symbol.
    /// </summary>
    /// <param name="prefix">An optional prefix to be added to the mangled name. This can be used to group related symbols together, such as all the symbols in a module or all the symbols in a function.</param>
    /// <returns>The mangled name of the symbol with the specified ID.</returns>
    public string RandName(string? prefix = null)
    {
        var prefixStr = "";
        if (!string.IsNullOrEmpty(prefix))
        {
            prefixStr = prefix + "__";
        }

        var count = 0;
        var len = NormalLength;
        while (true)
        {
            var name = prefixStr + RandString(len);
            if (_usedNames.TryAdd(name, true))
            {
                return name;
            }
            
            count++;
            if (count % 10 == 0)
            {
                len++;
            }
        }
    }

    private string RandString(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = ValidChars[_random.Next(ValidChars.Length)];
        }

        return new string(chars);
    }
}