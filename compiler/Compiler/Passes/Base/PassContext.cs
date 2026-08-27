using Nebra.Configuration;
using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The pass context is the context in which a pass is executed. It contains all the information that a pass needs to execute.
/// </summary>
public sealed class PassContext(DiagnosticsBag diag, List<PackageContext> pkgs, PackageContext? pkg, PreparsedFile? file, TypeTable types, IDAlloc<SymID> symAlloc, IDAlloc<ScopeID> scopeAlloc, IDAlloc<NodeID> nodeAlloc, NameMap names, Dictionary<string, object> cache, Config config)
{
    /// <summary>
    /// The configuration of the compiler. This is used to access language-level configuration such as the concat
    /// operator, index base, etc., which may be consulted by passes that need to know about language feature toggles.
    /// </summary>
    public Config Config { get; } = config;

    /// <summary>
    /// The diagnostics bag is used to collect diagnostics during the pass execution.
    /// </summary>
    public DiagnosticsBag Diag { get; } = diag;
    
    /// <summary>
    /// All package contexts in the current build. This is used to access the package contexts of other packages during
    /// the pass execution, such as for cross-package name resolution and type checking.
    /// </summary>
    public List<PackageContext> Pkgs { get; } = pkgs;
    
    /// <summary>
    /// The current package context being process. This can be null, if the pass is executed on the whole build at once,
    /// and the pass does not need to access the current package context.
    /// </summary>
    public PackageContext? Pkg { get; } = pkg;
    
    /// <summary>
    /// The current file being processed. This can be null, if the pass is executed on the whole build at once, and the
    /// pass does not need to access the current file.
    /// </summary>
    public PreparsedFile? File { get; } = file;
    
    /// <summary>
    /// The type table of the current build. This is used to access the type definitions and type information during the
    /// pass execution, such as for type checking and type inference.
    /// </summary>
    public TypeTable Types { get; } = types;
    
    /// <summary>
    /// The symbol allocator of the current build. This is used to allocate new symbol IDs during the pass execution.
    /// </summary>
    public IDAlloc<SymID> SymAlloc { get; } = symAlloc;
    
    /// <summary>
    /// The scope allocator of the current build. This is used to allocate new scope IDs during the pass execution.
    /// </summary>
    public IDAlloc<ScopeID> ScopeAlloc { get; } = scopeAlloc;

    public IDAlloc<NodeID> NodeAlloc { get; } = nodeAlloc;

    /// <summary>
    /// The name map of the current build. This is used to map symbol IDs to their original names and to their mangled
    /// names during the pass execution, such as for name resolution and name mangling.
    /// </summary>
    public NameMap Names { get; } = names;

    /// <summary>
    /// A cache that can be used to store intermediate results during the pass execution. This is useful for passes that
    /// need to store some state or some intermediate results that can be reused later in the pass execution.
    /// </summary>
    public Dictionary<string, object> Cache { get; } = cache;

    /// <summary>
    /// Creates a new pass context with the specified diagnostics bag, package contexts, current package context, current file,
    /// type table, symbol allocator, scope allocator, and name map. Used for passes that use the <see cref="PassScope.PerBuild"/>.
    /// </summary>
    public PassContext(DiagnosticsBag diag, List<PackageContext> pkgs, TypeTable types, IDAlloc<SymID> symAlloc, IDAlloc<ScopeID> scopeAlloc, IDAlloc<NodeID> nodeAlloc, NameMap names, Dictionary<string, object> cache, Config config)
        : this(diag, pkgs, null, null, types, symAlloc, scopeAlloc, nodeAlloc, names, cache, config)
    {
    }
}