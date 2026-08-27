namespace Nebra.Compiler.Annotations;

/// <summary>
/// Target kinds an annotation can be applied to. Must match the <c>AnnotationTarget</c>
/// enum declared in the Nebra stdlib (see <c>stdlib/annotation.d.sel</c>).
/// </summary>
public enum AnnotationTargetKind
{
    Function,
    LocalFunction,
    Variable,
    Parameter,
    Enum,
    EnumMember,
    Class,
    ClassMethod,
    ClassField,
    ClassConstructor,
    ClassAccessor,
    Interface,
    InterfaceMethod,
    InterfaceField,
}

/// <summary>
/// Parameter specification for a single annotation argument as declared by the annotation's
/// <c>meta.params</c> field.
/// </summary>
public sealed class AnnotationParamSpec(string name, string typeName, object? defaultValue, bool required)
{
    public string Name { get; } = name;
    /// <summary>One of <c>"string"</c>, <c>"number"</c>, <c>"boolean"</c>, <c>"table"</c> or an enum type name.</summary>
    public string TypeName { get; } = typeName;
    public object? DefaultValue { get; } = defaultValue;
    public bool Required { get; } = required;
}

/// <summary>
/// Fully resolved metadata of one registered annotation. Produced by the
/// <c>ResolveAnnotationsPass</c> and consumed by <c>ApplyAnnotationsPass</c>.
/// </summary>
public sealed class AnnotationDefinition(
    string name,
    HashSet<AnnotationTargetKind> targets,
    List<AnnotationParamSpec> parameters,
    string compiledLua,
    string sourcePath)
{
    public string Name { get; } = name;
    /// <summary>
    /// All target kinds this annotation accepts. A marker that works on both
    /// top-level functions and class methods (typical for nanos-style event
    /// hooks) has both <see cref="AnnotationTargetKind.Function"/> and
    /// <see cref="AnnotationTargetKind.ClassMethod"/> in this set.
    /// </summary>
    public HashSet<AnnotationTargetKind> Targets { get; } = targets;
    /// <summary>The first declared target; preserved for diagnostics and IDE labels.</summary>
    public AnnotationTargetKind PrimaryTarget => Targets.FirstOrDefault();
    public List<AnnotationParamSpec> Parameters { get; } = parameters;
    /// <summary>The transpiled Lua code for this annotation's definition file.</summary>
    public string CompiledLua { get; } = compiledLua;
    public string SourcePath { get; } = sourcePath;
}

/// <summary>
/// Compile-free counterpart of <see cref="AnnotationDefinition"/> used by the
/// LSP. Carries only the metadata (target + params) needed for IntelliSense
/// and arg validation; never holds compiled Lua. Built by
/// <c>ResolveAnnotationsPass.LoadMetaFromFile</c> directly from the source
/// file's AST.
/// </summary>
public sealed class AnnotationMeta(
    string name,
    HashSet<AnnotationTargetKind> targets,
    List<AnnotationParamSpec> parameters,
    string sourcePath)
{
    public string Name { get; } = name;
    public HashSet<AnnotationTargetKind> Targets { get; } = targets;
    public AnnotationTargetKind PrimaryTarget => Targets.FirstOrDefault();
    public List<AnnotationParamSpec> Parameters { get; } = parameters;
    public string SourcePath { get; } = sourcePath;
}

/// <summary>
/// Build-scoped registry of all annotations discovered from <c>Config.Annotations</c>.
/// Stored in <c>PassContext.Cache</c> under <see cref="CacheKey"/>.
/// </summary>
public sealed class AnnotationRegistry
{
    public const string CacheKey = "Annotations";

    private readonly Dictionary<string, AnnotationDefinition> _byName = new(StringComparer.Ordinal);

    public bool TryAdd(AnnotationDefinition def) => _byName.TryAdd(def.Name, def);

    public bool TryGet(string name, out AnnotationDefinition def)
    {
        var found = _byName.TryGetValue(name, out var d);
        def = d!;
        return found;
    }

    public IReadOnlyCollection<AnnotationDefinition> All => _byName.Values;

    public int Count => _byName.Count;
}
