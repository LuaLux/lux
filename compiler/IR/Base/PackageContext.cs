namespace Nebra.IR;

/// <summary>
/// Represents the context of a package, which contains all the information about the package. The package context is
/// used to store and manage all the information about a package during the compilation process.
/// </summary>
public sealed class PackageContext(string path, SymbolArena syms, ScopeGraph scopes, TypeTable types, ScopeID root)
{
    /// <summary>
    /// The path of the package (directory path).
    /// </summary>
    public string Path { get; } = path;

    /// <summary>
    /// A list of all files in the package. Each file is represented as a PreparsedFile, which contains the filename, content, and HIR of the file.
    /// </summary>
    public List<PreparsedFile> Files { get; } = [];
    
    /// <summary>
    /// The symbol arena used for this package, which contains all the symbols defined in this package. 
    /// </summary>
    public SymbolArena Syms { get; } = syms;
    
    /// <summary>
    /// The scope graph of this package, which contains all the scopes and their relationships in this package.
    /// </summary>
    public ScopeGraph Scopes { get; } = scopes;
    
    /// <summary>
    /// The type table of this package, which contains all the types defined in this package.
    /// </summary>
    public TypeTable Types { get; } = types;
    
    /// <summary>
    /// The root scope ID of this package.
    /// </summary>
    public ScopeID Root { get; } = root;
}