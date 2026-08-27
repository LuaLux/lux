namespace Nebra.Configuration;

/// <summary>
/// The <c>[reflection]</c> section. Controls emission of runtime reflection metadata — a global
/// registry (<c>_G.__nebra_reflect</c>) describing the program's types, populated by generated code
/// and read at runtime through the <c>reflect</c> library.
/// </summary>
public sealed class ReflectionSection
{
    /// <summary>How much metadata to emit. See <see cref="ReflectionMode"/>. Defaults to
    /// <see cref="ReflectionMode.All"/>; <c>none</c> and <c>annotated</c> are opt-in optimizations.</summary>
    public ReflectionMode Mode { get; set; } = ReflectionMode.All;

    internal void Merge(ReflectionSection section)
    {
        Mode = Config.MergeVal(Mode, section.Mode, ReflectionMode.All);
    }
}
