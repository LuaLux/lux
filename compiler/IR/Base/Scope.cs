using Nebra.Diagnostics;

namespace Nebra.IR;

/// <summary>
/// Represents a scope in the IR that can contain symbols and other scopes. A scope is a hierarchical structure that
/// defines the visibility and lifetime of symbols in the IR. Each scope can have a parent scope and multiple child
/// scopes, forming a tree-like structure. Scopes are used to manage the symbol table and to resolve symbol references
/// in the IR.
/// </summary>
public sealed class Scope(ScopeID id, ScopeID parent)
{
    /// <summary>
    /// A special value that represents the global scope. The global scope is the root of the scope hierarchy and contains all the top-level symbols in the IR.
    /// It has no parent and no valid ID, and is used as the default scope for symbols that are not declared in any other scope. 
    /// </summary>
    public static readonly Scope Global = new(ScopeID.Invalid, ScopeID.Invalid);
    
    /// <summary>
    /// The unique ID of the scope.
    /// </summary>
    public ScopeID ID { get; } = id;
    
    /// <summary>
    /// The ID of the parent scope. 
    /// </summary>
    public ScopeID Parent { get; } = parent;
    
    /// <summary>
    /// Whether the scope is the global scope. This is true if the parent scope is invalid, which indicates that the scope has no parent and is the root of the scope hierarchy.
    /// </summary>
    public bool IsInGlobalScope => Parent == ScopeID.Invalid;
    
    /// <summary>
    /// A dictionary mapping names to lists of symbol IDs. This is used to store the symbols declared in the scope.
    /// Normally, each name should only have exactly one symbole ID but in the case of the discard symbol (_) there can
    /// be multiple symbol IDs with the same name. The list of symbol IDs allows us to handle this case without losing
    /// any information about the symbols declared in the scope.
    /// </summary>
    public Dictionary<string, List<SymID>> Symbols { get; } = new();
    
    /// <summary>
    /// An implicit conversion operator that allows a Scope to be implicitly converted to its ScopeID.
    /// </summary>
    public static implicit operator ScopeID(Scope scope) => scope.ID;
    
    /// <summary>
    /// Adds a symbol with the specified name and ID to the scope. If the name already exists in the scope, the symbol
    /// ID is added to the list of symbol IDs associated with that name.
    /// </summary>
    /// <param name="name">The original name of the symbol to be added to the scope. This is used as the key in the Symbols dictionary to store the symbol ID(s) associated with the name.</param>
    /// <param name="sym">The unique ID of the symbol to be added to the scope. This is added to the list of symbol IDs associated with the name in the Symbols dictionary.</param>
    public void AddSymbol(string name, SymID sym)
    {
        if (!Symbols.TryGetValue(name, out var syms))
        {
            syms = [];
            Symbols[name] = syms;
        }
        
        syms.Add(sym);
    }

    /// <summary>
    /// Returns the list of symbol IDs associated with the specified name in the scope, if any. If the name does not exist in
    /// the scope, this method returns false and sets the output parameter to an empty list. 
    /// </summary>
    /// <param name="name">The original name of the symbol to look up in the scope.</param>
    /// <param name="syms">The output parameter that will contain the list of symbol IDs associated with the specified name if it exists in the scope; otherwise, an empty list.</param>
    /// <returns>true if the specified name exists in the scope and the output parameter contains the list of symbol IDs associated with that name; otherwise, false.</returns>
    public bool GetSymbols(string name, out List<SymID> syms)
    {
        syms = [];
        if (Symbols.TryGetValue(name, out var symsList))
        {
            syms = symsList;
            return true;
        }
        
        return false;
    }
}

/// <summary>
/// Represents the scope graph of the IR. The scope graph is a hierarchical structure that represents the scopes in the
/// IR and their relationships. It is used to manage the symbol table and to resolve symbol references in the IR.
/// </summary>
public sealed class ScopeGraph
{
    /// <summary>
    /// The ID of the root scope of the scope graph.
    /// </summary>
    public ScopeID Root { get; }

    private readonly IDAlloc<ScopeID> _scopeAlloc;
    private readonly DiagnosticsBag _diag;

    private readonly Dictionary<ScopeID, Scope> _byID = new();
    private readonly Dictionary<NodeID, ScopeID> _nodeScope = new();

    /// <summary>
    /// Creates a new scope graph with the specified diagnostics bag and scope ID allocator. The scope graph is
    /// initialized with a newly created root scope.
    /// </summary>
    /// <param name="diag"></param>
    /// <param name="scopeAlloc"></param>
    public ScopeGraph(DiagnosticsBag diag, IDAlloc<ScopeID> scopeAlloc)
    {
        _diag = diag;
        _scopeAlloc = scopeAlloc;
        Root = NewScope();
    }

    /// <summary>
    /// Creates a new scope with the specified parent scope and adds it to the scope graph. The new scope is assigned a
    /// unique ID using the scope ID allocator, and is added to the underlying dictionary for lookup and retrieval.
    /// </summary>
    /// <param name="parent">The optional ID of the parent scope. If this is null, the new scope will be created with the global scope as its parent.</param>
    /// <returns>The unique ID of the newly created scope.</returns>
    public ScopeID NewScope(ScopeID? parent = null)
    {
        var id = _scopeAlloc.Next();
        var scope = new Scope(id, parent ?? ScopeID.Invalid);
        _byID[id] = scope;
        return id;
    }

    /// <summary>
    /// Binds the specified node to the specified scope. This is used to associate a node in the IR with the scope in
    /// which it is declared, which is necessary for name resolution and other semantic analysis tasks.
    /// </summary>
    /// <param name="node">The ID of the node to be bound to the scope.</param>
    /// <param name="scope">The ID of the scope to which the node should be bound.</param>
    public void BindNode(NodeID node, ScopeID scope)
    {
        _nodeScope[node] = scope;
    }

    /// <summary>
    /// Returns the ID of the scope that encloses the specified node, if any. If the node is not bound to any scope,
    /// this method returns false and sets the output parameter to an invalid scope ID.
    /// </summary>
    /// <param name="node">The ID of the node for which to find the enclosing scope.</param>
    /// <param name="scope">The output parameter that will contain the ID of the enclosing scope if the node is bound to a scope; otherwise, an invalid scope ID.</param>
    /// <returns>true if the node is bound to a scope and the output parameter contains the ID of the enclosing scope; otherwise, false.</returns>
    public bool EnclosingScope(NodeID node, out ScopeID scope)
    {
        scope = ScopeID.Invalid;
        if (_nodeScope.TryGetValue(node, out var nodeScope))
        {
            scope = nodeScope;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Declares a symbol with the specified name and ID in the specified scope. For functions, multiple symbols with
    /// the same name can be declared in the same scope, as long as they have different signatures (overloading).
    /// For other symbols redeclaration is not allowed and will result in an error being reported to the diagnostics bag.
    /// </summary>
    /// <param name="scope">The ID of the scope in which to declare the symbol.</param>
    /// <param name="name">The original name of the symbol to be declared.</param>
    /// <param name="sym">The unique ID of the symbol to be declared.</param>
    /// <param name="arena">The symbol arena in which the symbol to be declared is stored. This is used to look up the symbol by its ID and to report errors if necessary.</param>
    /// <returns></returns>
    public bool DeclareSymbol(ScopeID scope, string name, SymID sym, SymbolArena arena, TextSpan? span = null)
    {
        var reportSpan = span ?? TextSpan.Empty;

        if (!_byID.TryGetValue(scope, out var sc))
        {
            _diag.Report(reportSpan, DiagnosticCode.ErrDeclaringInNonExistingScope, name, scope);
            return false;
        }

        if (name == "_")
        {
            sc.AddSymbol(name, sym);
            return true;
        }

        if (!arena.GetByID(sym, out var newSymbol))
        {
            _diag.Report(reportSpan, DiagnosticCode.ErrDeclaringNonExistingSymbol, name, sym);
            return false;
        }

        if (sc.GetSymbols(name, out var existingSyms))
        {
            if (newSymbol.Kind == SymbolKind.Function)
            {
                foreach (var existingSymID in existingSyms)
                {
                    if (!arena.GetByID(existingSymID, out var existingSym))
                    {
                        _diag.Report(reportSpan, DiagnosticCode.ErrDeclaringNonExistingSymbol, name, existingSymID);
                        continue;
                    }

                    if (existingSym.Kind != SymbolKind.Function)
                    {
                        _diag.Report(reportSpan, DiagnosticCode.ErrRedeclaration, name, scope);
                        return false;
                    }
                }
            }
            else
            {
                _diag.Report(reportSpan, DiagnosticCode.ErrRedeclaration, name, scope);
                return false;
            }
        }

        sc.AddSymbol(name, sym);
        return true;
    }

    /// <summary>
    /// Looks up a symbol with the specified name in the specified scope. This method only looks up symbols declared in
    /// the specified scope, and does not look up symbols declared in parent scopes.
    /// </summary>
    /// <param name="scope">The ID of the scope in which to look up the symbol.</param>
    /// <param name="name">The original name of the symbol to look up.</param>
    /// <param name="sym">The output parameter that will contain the unique ID of the symbol with the specified name if it exists in the specified scope; otherwise, an invalid symbol ID.</param>
    /// <returns>true if a symbol with the specified name exists in the specified scope and the output parameter contains its unique ID; otherwise, false.</returns>
    public bool LookupOnlyCurrent(ScopeID scope, string name, out SymID sym)
    {
        if (!_byID.TryGetValue(scope, out var sc))
        {
            _diag.Report(TextSpan.Empty, DiagnosticCode.ErrLookingUpInNonExistingScope, name, scope);
            sym = SymID.Invalid;
            return false;
        }

        if (sc.GetSymbols(name, out var syms))
        {
            if (syms.Count > 0)
            {
                sym = syms[0];
                return true;
            }
        }

        sym = SymID.Invalid;
        return false;
    }

    /// <summary>
    /// Looks up a symbol with the specified name in the specified scope and its parent scopes. This method looks up
    /// symbols declared in the specified scope first, and if no symbol with the specified name is found, it looks up
    /// symbols declared in the parent scope, and so on, until it reaches the global scope.
    /// </summary>
    /// <param name="scope">The ID of the scope in which to start looking up the symbol. This is the scope in which the symbol reference is located, and is used to determine the visibility of symbols declared in parent scopes.</param>
    /// <param name="name">The original name of the symbol to look up. This is used as the key in the Symbols dictionary to look up the symbol ID(s) associated with the name.</param>
    /// <param name="sym">The output parameter that will contain the unique ID of the symbol with the specified name if it exists in the specified scope or any of its parent scopes; otherwise, an invalid symbol ID.</param>
    /// <returns>true if a symbol with the specified name exists in the specified scope or any of its parent scopes and the output parameter contains its unique ID; otherwise, false.</returns>
    public bool Lookup(ScopeID scope, string name, out SymID sym)
    {
        var currentScope = scope;
        while (currentScope != ScopeID.Invalid)
        {
            if (LookupOnlyCurrent(currentScope, name, out sym))
            {
                return true;
            }

            if (!_byID.TryGetValue(currentScope, out var sc))
            {
                _diag.Report(TextSpan.Empty, DiagnosticCode.ErrLookingUpInNonExistingScope, name, currentScope);
                sym = SymID.Invalid;
                return false;
            }

            currentScope = sc.Parent;
        }

        sym = SymID.Invalid;
        return false;
    }

    /// <summary>
    /// Looks up all symbols with the specified name in the specified scope and its parent scopes. This method looks up
    /// symbols declared in the specified scope first, and if no symbol with the specified name is found, it looks up
    /// symbols declared in the parent scope, and so on, until it reaches the global scope. This method returns a list
    /// of symbol IDs associated with the specified name, which can contain multiple symbol IDs in the case of the
    /// discard symbol (_) or in the case of function overloading.
    /// </summary>
    /// <param name="scope">The ID of the scope in which to start looking up the symbols. This is the scope in which the symbol reference is located, and is used to determine the visibility of symbols declared in parent scopes.</param>
    /// <param name="name">The original name of the symbols to look up. This is used as the key in the Symbols dictionary to look up the symbol ID(s) associated with the name.</param>
    /// <returns>A list of unique IDs of the symbols with the specified name that exist in the specified scope or any of its parent scopes. If no symbol with the specified name exists in the specified scope or any of its parent scopes, this method returns an empty list.</returns>
    public List<SymID> LookupAll(ScopeID scope, string name)
    {
        var currentScope = scope;
        while (currentScope != ScopeID.Invalid)
        {
            if (_byID.TryGetValue(currentScope, out var sc))
            {
                if (sc.GetSymbols(name, out var currentSyms) && currentSyms.Count > 0)
                {
                    return currentSyms;
                }

                currentScope = sc.Parent;
            }
            else
            {
                _diag.Report(TextSpan.Empty, DiagnosticCode.ErrLookingUpInNonExistingScope, name, currentScope);
                return [];
            }
        }

        return [];
    }
    
    /// <summary>
    /// Returns the ID of the parent scope of the specified scope, if any. If the specified scope does not exist in the
    /// scope graph, this method returns false and sets the output parameter to an invalid scope ID.
    /// </summary>
    /// <param name="scope">The ID of the scope for which to find the parent scope.</param>
    /// <param name="parent">The output parameter that will contain the ID of the parent scope if the specified scope exists in the scope graph; otherwise, an invalid scope ID.</param>
    /// <returns>true if the specified scope exists in the scope graph and the output parameter contains the ID of its parent scope; otherwise, false.</returns>
    public bool ParentScope(ScopeID scope, out ScopeID parent)
    {
        parent = ScopeID.Invalid;
        if (!_byID.TryGetValue(scope, out var sc))
        {
            _diag.Report(TextSpan.Empty, DiagnosticCode.ErrLookingUpInNonExistingScope, scope);
            return false;
        }

        parent = sc.Parent;
        return true;
    }
}

/// <summary>
/// Represents a stack of scopes that can be used during name resolution and other semantic analysis tasks. The scope
/// stack is a simple wrapper around a stack of scope IDs that allows us to keep track of the current scope during
/// traversal of the IR. When we enter a new scope, we push its ID onto the stack, and when we exit a scope, we pop its
/// ID from the stack. This allows us to easily determine the current scope at any point during traversal, and to look
/// up symbols in the current scope and its parent scopes using the scope graph.
/// </summary>
public sealed class ScopeStack
{
    /// <summary>
    /// Returns the ID of the current scope, which is the scope at the top of the stack. If the stack is empty, this
    /// property returns an invalid scope ID.
    /// </summary>
    public ScopeID Current => _stack.Peek();
    
    private readonly Stack<ScopeID> _stack = [];
    
    /// <summary>
    /// Pushes the current scope onto the stack.
    /// </summary>
    /// <param name="scope">The ID of the scope to be pushed onto the stack.</param>
    public void Push(ScopeID scope)
    {
        _stack.Push(scope);
    }
    
    /// <summary>
    /// Pops the current scope from the stack. This is used to exit the current scope and return to the parent scope during traversal of the IR.
    /// </summary>
    public void Pop()
    {
        _stack.Pop();
    }
}