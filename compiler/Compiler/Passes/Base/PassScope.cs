namespace Nebra.Compiler.Passes;

/// <summary>
/// Defines the scope in which a pass is executed.
/// </summary>
public enum PassScope
{
    /// <summary>
    /// The pass is executed on each file separately.
    /// </summary>
    PerFile,
    /// <summary>
    /// The pass is executed on the whole build at once.
    /// </summary>
    PerBuild,
}