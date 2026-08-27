namespace Nebra.Configuration;

/// <summary>
/// The [code] section of the <see cref="Config"/>. The code section is used to configure specific parts of the code
/// generation and to solve fuck-ups of the Lua creators.
/// </summary>
public sealed class CodeSection
{
    /// <summary>
    /// Overrides the index base used by Lua. Every good developer hates Lua's "wE ArE cOOL ANd SpECiAl aND StaRT aT 1".
    /// </summary>
    public int IndexBase { get; set; } = 0;
    
    /// <summary>
    /// Overrides the concat operator. Although this will just ADD the possiblity to use "+" in a string based context.
    /// </summary>
    public string ConcatOperator { get; set; } = "+";
    
    /// <summary>
    /// Allows `Hello {name}`. Which translates to "Hello " .. tostring(name).
    /// </summary>
    public bool StringInterpolation  { get; set; } = true;

    /// <summary>
    /// Enables alternative, C-style boolean operators: <c>&amp;&amp;</c> for <c>and</c>, <c>||</c> for <c>or</c>,
    /// <c>!</c> (prefix) for <c>not</c> and <c>!=</c> for <c>~=</c>. When disabled, using these forms emits a
    /// diagnostic. The Lua-style operators are always accepted.
    /// </summary>
    public bool AltBooleanOperators { get; set; } = true;

    /// <summary>
    /// Sets the policy on how to handle semicolons in code.
    /// </summary>
    public Policy Semicolons { get; set; } = Policy.Optional;

    /// <summary>
    /// Sets the code of the import statement that is used by the transpiler. %s is replaced by the string to define the imported file.
    /// </summary>
    public string ImportStatement { get; set; } = "require(%s)";

    /// <summary>
    /// Optional suffix appended to every <c>import</c> path before it is substituted
    /// into <see cref="ImportStatement"/>. Nebra always strips a trailing <c>.neb</c>
    /// from the source path first — the writer-visible filename is never legal in
    /// the lowered Lua. Set this to <c>".lua"</c> for runtimes whose loader
    /// requires the extension (e.g. nanos-world's <c>Package.Require("Foo.lua")</c>).
    /// Default is empty, which matches Lua's classic <c>require("foo")</c>.
    /// </summary>
    public string ImportExtension { get; set; } = "";

    /// <summary>
    /// Whether to strip unused variables, functions, and other declarations from the generated code. This can help to
    /// reduce the size of the generated code and improve performance, but it may also make debugging more difficult if
    /// you need to inspect the generated code.
    /// </summary>
    public bool StripUnused { get; set; } = true;

    public List<string> Libs { get; set; } = [];

    /// <summary>
    /// Module paths that are implicitly namespace-imported at the top of every
    /// source file. Each entry becomes <c>import * as &lt;ns&gt; from "&lt;entry&gt;"</c>,
    /// where the namespace name is derived from the path: the last segment, or
    /// the parent segment if the last is <c>Index</c>. Mirrors the
    /// "shared globals everywhere" pattern of runtimes that auto-load a
    /// shared module on every side (nanos-world's <c>Shared/Index.lua</c>).
    /// Files matching an auto-import entry don't get their own import (no
    /// self-reference cycles).
    /// </summary>
    public List<string> AutoImports { get; set; } = [];

    /// <summary>
    /// When false, auto-imports are visible to the compiler / LSP for name
    /// resolution and type checking, but the <c>require(...)</c> call is
    /// <em>not</em> emitted into the lowered Lua. Use this when the host
    /// runtime pre-loads the module itself (e.g. nanos-world loads
    /// <c>Shared/Index.lua</c> on every side before any other script
    /// runs — emitting our own <c>Package.Require</c> would re-execute it).
    /// Default <c>true</c>: emit a real require so plain Lua projects keep
    /// working.
    /// </summary>
    public bool AutoImportsEmit { get; set; } = true;

    internal void Merge(CodeSection section)
    {
        IndexBase = Config.MergeVal(IndexBase, section.IndexBase, 0);
        ConcatOperator = Config.MergeVal(ConcatOperator, section.ConcatOperator, "+");
        StringInterpolation = Config.MergeVal(StringInterpolation, section.StringInterpolation, true);
        AltBooleanOperators = Config.MergeVal(AltBooleanOperators, section.AltBooleanOperators, false);
        Semicolons = Config.MergeVal(Semicolons, section.Semicolons, Policy.Optional);
        ImportStatement = Config.MergeVal(ImportStatement, section.ImportStatement, "require(%s)");
        ImportExtension = Config.MergeVal(ImportExtension, section.ImportExtension, "");
        StripUnused = Config.MergeVal(StripUnused, section.StripUnused, true);
        if (section.Libs.Count > 0) Libs = section.Libs;
        if (section.AutoImports.Count > 0) AutoImports = section.AutoImports;
        AutoImportsEmit = Config.MergeVal(AutoImportsEmit, section.AutoImportsEmit, true);
    }
}