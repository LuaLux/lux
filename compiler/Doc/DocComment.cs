namespace Nebra.Doc;

/// <summary>
/// A parsed LuaCATS-style documentation comment attached to a declaration.
/// The free-form summary lives in <see cref="Summary"/>; tagged fragments
/// (<c>@param</c>, <c>@return</c>, …) are split out so consumers can render
/// or query them independently.
/// </summary>
public sealed class DocComment
{
    /// <summary>
    /// Free-form text that appeared before any <c>@</c> tag. The first
    /// non-empty line is treated as the short summary; later paragraphs go
    /// into <see cref="Remarks"/>.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Additional paragraphs after <see cref="Summary"/>.</summary>
    public string Remarks { get; init; } = string.Empty;

    public List<DocParam> Params { get; init; } = [];
    public List<DocReturn> Returns { get; init; } = [];
    public List<string> See { get; init; } = [];
    public List<string> Examples { get; init; } = [];
    public List<DocOverload> Overloads { get; init; } = [];
    public List<DocGeneric> Generics { get; init; } = [];

    public bool Deprecated { get; init; }

    /// <summary>Optional reason supplied with <c>@deprecated &lt;reason&gt;</c>.</summary>
    public string? DeprecatedReason { get; init; }

    public bool Async { get; init; }
    public bool NoDiscard { get; init; }
    public string? Since { get; init; }
    public DocVisibility Visibility { get; init; } = DocVisibility.Default;

    /// <summary>Unknown/extension <c>@</c>-tags, keyed by tag name.</summary>
    public Dictionary<string, List<string>> Custom { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty =>
        string.IsNullOrEmpty(Summary)
        && string.IsNullOrEmpty(Remarks)
        && Params.Count == 0
        && Returns.Count == 0
        && See.Count == 0
        && Examples.Count == 0
        && Overloads.Count == 0
        && Generics.Count == 0
        && !Deprecated && !Async && !NoDiscard
        && Since == null
        && Visibility == DocVisibility.Default
        && Custom.Count == 0;
}

public enum DocVisibility { Default, Public, Private, Protected, Package }

public sealed class DocParam(string name, bool optional, string? typeText, string description)
{
    public string Name { get; } = name;
    public bool Optional { get; } = optional;
    public string? TypeText { get; } = typeText;
    public string Description { get; } = description;
}

public sealed class DocReturn(string? name, string? typeText, string description)
{
    public string? Name { get; } = name;
    public string? TypeText { get; } = typeText;
    public string Description { get; } = description;
}

public sealed class DocGeneric(string name, string? bound)
{
    public string Name { get; } = name;
    public string? Bound { get; } = bound;
}

public sealed class DocOverload(string raw)
{
    public string Raw { get; } = raw;
}
