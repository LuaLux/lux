namespace Nebra.Doc;

/// <summary>
/// Static, format-agnostic model of a documentation site: every module known
/// to the project, its file-level doc, and the public symbols it exposes.
/// Built once by <see cref="DocSiteBuilder"/> and consumed by the markdown
/// and HTML renderers.
/// </summary>
public sealed class DocSite
{
    public string ProjectName { get; init; } = "nebra project";
    public string? ProjectVersion { get; init; }
    public List<DocModule> Modules { get; init; } = [];
}

public sealed class DocModule
{
    /// <summary>Slash-separated path used as the page slug, e.g. <c>utils/string</c>.</summary>
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public DocComment? Doc { get; init; }
    public List<DocSymbol> Symbols { get; init; } = [];
}

public enum DocSymbolKind { Function, Class, Interface, Enum, Variable }
public enum DocMemberKind { Field, Method, Constructor, Accessor, EnumMember }

public sealed class DocSymbol
{
    public required string Name { get; init; }
    public required DocSymbolKind Kind { get; init; }
    public required string Signature { get; init; }
    public DocComment? Doc { get; init; }
    public List<DocMember> Members { get; init; } = [];
    public List<string> BaseTypes { get; init; } = [];
    public List<string> Implements { get; init; } = [];
}

public sealed class DocMember
{
    public required string Name { get; init; }
    public required DocMemberKind Kind { get; init; }
    public required string Signature { get; init; }
    public DocComment? Doc { get; init; }
    public bool IsStatic { get; init; }
    public bool IsProtected { get; init; }
    public bool IsAbstract { get; init; }
}
