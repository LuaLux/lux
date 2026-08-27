using Nebra.Diagnostics;
using Nebra.IR;

namespace Nebra.Compiler.Passes;

/// <summary>
/// The pass manager is responsible for managing the passes and executing them in the correct order. It also provides a
/// way to register new passes and to get the registered passes by their names.
/// </summary>
public sealed class PassManager
{
    /// <summary>
    /// The full compiler pipeline, which is the default order of passes that are executed when the compiler is run without any custom pass selection.
    /// </summary>
    public static readonly string[] CompilerPipeline = [
        ResolvePackagesPass.PassName,
        ResolveAnnotationsPass.PassName,
        ApplyAnnotationsPass.PassName,
        ResolveLibsPass.PassName,
        BindDeclarePass.PassName,
        ResolveImportsPass.PassName,
        ResolveNamesPass.PassName,
        ResolveTypeRefsPass.PassName,
        CheckSidesPass.PassName,
        CheckImmutabilityPass.PassName,
        InferTypesPass.PassName,
        CheckMemberSidesPass.PassName,
        ValidateGenericConstraintsPass.PassName,
        DetectUnusedPass.PassName,
        DeclGenPass.PassName,
        ManglePase.PassName,
        CodegenPass.PassName
    ];
    /// <summary>
    /// The check pipeline, which is a subset of the full compiler pipeline that includes only the passes that are necessary for type checking and name resolution.
    /// This pipeline can be used for faster feedback during development, as it skips the passes that are responsible for code generation and mangling.
    /// </summary>
    public static readonly string[] CheckPipeline = [
        ResolvePackagesPass.PassName,
        ResolveAnnotationsPass.PassName,
        ApplyAnnotationsPass.PassName,
        ResolveLibsPass.PassName,
        BindDeclarePass.PassName,
        ResolveImportsPass.PassName,
        ResolveNamesPass.PassName,
        ResolveTypeRefsPass.PassName,
        CheckSidesPass.PassName,
        CheckImmutabilityPass.PassName,
        InferTypesPass.PassName,
        CheckMemberSidesPass.PassName,
        ValidateGenericConstraintsPass.PassName,
        DetectUnusedPass.PassName
    ];

    public static readonly string[] SingleFilePipeline = [
        ResolveLibsPass.PassName,
        BindDeclarePass.PassName,
        ResolveNamesPass.PassName,
        ResolveTypeRefsPass.PassName,
        CheckSidesPass.PassName,
        CheckImmutabilityPass.PassName,
        InferTypesPass.PassName,
        CheckMemberSidesPass.PassName,
        ValidateGenericConstraintsPass.PassName,
        DetectUnusedPass.PassName
    ];

    public static readonly string[] SingleFilePhase1 = [
        ResolveLibsPass.PassName,
        BindDeclarePass.PassName,
        ResolveNamesPass.PassName
    ];

    public static readonly string[] SingleFilePhase2 = [
        ResolveTypeRefsPass.PassName,
        CheckSidesPass.PassName,
        CheckImmutabilityPass.PassName,
        InferTypesPass.PassName,
        CheckMemberSidesPass.PassName,
        ValidateGenericConstraintsPass.PassName,
        DetectUnusedPass.PassName
    ];

    /// <summary>
    /// Pipeline used by <see cref="ResolveAnnotationsPass"/> to sub-compile annotation definition files.
    /// Skips <see cref="ResolveAnnotationsPass"/> and <see cref="ApplyAnnotationsPass"/> to prevent
    /// recursive loading and also skips imports (annotation files are standalone).
    /// </summary>
    public static readonly string[] AnnotationFilePipeline = [
        ResolveLibsPass.PassName,
        BindDeclarePass.PassName,
        CodegenPass.PassName
    ];
    
    private readonly Dictionary<string, Pass> _passes = new();
    private readonly List<string> _passOrder = [];

    /// <summary>
    /// Initializes a new instance of the PassManager class and registers the default passes.
    /// </summary>
    public PassManager()
    {
        Register(new ResolvePackagesPass());
        Register(new ResolveAnnotationsPass());
        Register(new ApplyAnnotationsPass());
        Register(new ResolveLibsPass());
        Register(new BindDeclarePass());
        Register(new ResolveImportsPass());
        Register(new ResolveNamesPass());
        Register(new ResolveTypeRefsPass());
        Register(new CheckSidesPass());
        Register(new CheckImmutabilityPass());
        Register(new InferTypesPass());
        Register(new CheckMemberSidesPass());
        Register(new ValidateGenericConstraintsPass());
        Register(new DetectUnusedPass());
        Register(new DeclGenPass());
        Register(new ManglePase());
        Register(new CodegenPass());
    }

    /// <summary>
    /// Registers a new pass to the pass manager.
    /// </summary>
    /// <param name="pass">The pass to be registered. The pass must have a unique name, and the name must not conflict with the names of other passes.</param>
    public void Register(Pass pass)
    {
        if (!_passes.TryAdd(pass.Name, pass))
        {
            throw new InvalidOperationException($"A pass with the name '{pass.Name}' is already registered.");
        }
    }

    /// <summary>
    /// Builds the pass order based on the selected passes. The selected passes are the names of the passes that are
    /// selected to be executed in the current build. The pass manager will build the pass order based on the
    /// dependencies between the selected passes, and will ensure that the passes are executed in the correct
    /// order. If there are any circular dependencies between the selected passes, an exception will be thrown.
    /// </summary>
    /// <param name="selectedPasses">
    /// The names of the passes that are selected to be executed in the current build. The pass manager will build the
    /// pass order based on the dependencies between these passes.
    /// </param>
    public void BuildOrder(string[] selectedPasses)
    {
        var seen = new HashSet<string>();
        var tmp = new HashSet<string>();
        
        _passOrder.Clear();
        
        foreach (var passName in selectedPasses)
        {
            Visit(passName);
        }
        
        return;
        
        void Visit(string passName)
        {
            if (seen.Contains(passName))
            {
                return;
            }
            
            if (!tmp.Add(passName))
            {
                throw new InvalidOperationException($"Circular dependency detected between passes: {passName}");
            }

            if (_passes.TryGetValue(passName, out var pass))
            {
                foreach (var dep in pass.Dependencies)
                {
                    Visit(dep);
                }
            }
            
            tmp.Remove(passName);
            seen.Add(passName);
            
            _passOrder.Add(passName);
        }
    }

    /// <summary>
    /// Runs the passes in the order determined by the BuildOrder method. The passes are executed on the given context, which
    /// contains all the information that the passes need to execute. The pass manager will execute the passes in the correct
    /// order, and will ensure that the passes are executed on the correct scope (per package, per file, or per build) based on
    /// the scope of each pass. If any pass fails during execution, the pass manager will stop executing the remaining
    /// passes and return false to indicate that the compilation process should be aborted.
    /// </summary>
    /// <param name="diag">The diagnostics bag that is used to report any errors or warnings during the pass execution.</param>
    /// <param name="pkgs">The list of packages that are being compiled.</param>
    /// <param name="types">The type table that contains all the types that are defined in the source code.</param>
    /// <param name="symAlloc">The symbol ID allocator that is used to allocate new symbol IDs for the symbols that are defined during the pass execution.</param>
    /// <param name="scopeAlloc">The scope ID allocator that is used to allocate new scope IDs for the scopes that are defined during the pass execution.</param>
    /// <param name="names">The name map that is used to manage the original and mangled names of the symbols that are defined during the pass execution.</param>
    /// <param name="cache">The cache that is used to store any intermediate results or data that the passes may need to share during the pass execution.</param>
    /// <param name="config">The configuration that contains all the settings and options that may affect the behavior of the passes during the pass execution.</param>
    /// <returns>true if all the passes executed successfully, or false if any pass failed and the compilation process should be aborted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a pass has an invalid scope.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a pass is not registered.</exception>
    public bool Run(DiagnosticsBag diag, List<PackageContext> pkgs, TypeTable types, IDAlloc<SymID> symAlloc,
        IDAlloc<ScopeID> scopeAlloc, IDAlloc<NodeID> nodeAlloc, NameMap names, Dictionary<string, object> cache, Configuration.Config config)
    {
        if (_passOrder.Count == 0)
        {
            throw new InvalidOperationException("Pass order is not built. Please call BuildOrder before running the passes.");
        }

        foreach (var passName in _passOrder)
        {
            if (_passes.TryGetValue(passName, out var pass))
            {
                if (pass.NoErrors && diag.HasErrors)
                {
                    return false;
                }

                switch (pass.Scope)
                {
                    case PassScope.PerFile:
                        if (pkgs.Any(pkg => pkg.Files.Select(file => new PassContext(diag, pkgs, pkg, file, types, symAlloc, scopeAlloc, nodeAlloc, names, cache, config)).Any(ctx => !pass.Run(ctx))))
                        {
                            return false;
                        }
                        break;
                    case PassScope.PerBuild:
                        if (!pass.Run(new PassContext(diag, pkgs, types, symAlloc, scopeAlloc, nodeAlloc, names, cache, config)))
                        {
                            return false;
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("Invalid pass scope: " + pass.Scope);
                }
            }
            else
            {
                throw new InvalidOperationException($"Pass '{passName}' is not registered.");
            }
        }
        
        return true;
    }
}