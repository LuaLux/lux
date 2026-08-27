namespace Nebra.Compiler.Passes;

/// <summary>
/// Represents a compiler pass in an abstract way. A compiler pass is a transformation or analysis that is applied to
/// the intermediate representation (IR) of the source code during the compilation process. Each pass can perform
/// different operations on the IR.
/// </summary>
public abstract class Pass(string name, PassScope scope, bool noErrors = false, params string[] dependencies)
{
    /// <summary>
    /// The name of the pass. Used to distinguish the pass from other passes.
    /// </summary>
    public string Name { get; } = name;
    
    /// <summary>
    /// The scope on which the pass is executed.
    /// </summary>
    public PassScope Scope { get; } = scope;
    
    /// <summary>
    /// The flag indicating whether the pass is required to be only executed, if no errors have been reported in the
    /// previous passes.
    /// </summary>
    public bool NoErrors { get; } = noErrors;
    
    /// <summary>
    /// The dependencies of the pass. This is a list of pass names that this pass depends on, meaning that those passes
    /// must be executed before this pass can be executed.
    /// </summary>
    public string[] Dependencies { get; } = dependencies;
    
    /// <summary>
    /// Runs the pass on the given pass context. This is the main method that performs the actual transformation or
    /// analysis of the IR during the pass execution.
    /// </summary>
    /// <param name="context">The pass context that contains all the information that the pass needs to execute.</param>
    /// <returns>Returns true if the pass executed successfully, or false if the pass failed and the compilation process should be aborted.</returns>
    public abstract bool Run(PassContext context);
}