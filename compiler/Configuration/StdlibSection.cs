namespace Nebra.Configuration;

/// <summary>
/// The [stdlib] section of the <see cref="Config"/>. Controls which Lua standard library
/// declarations are loaded into the type universe.
/// </summary>
/// <remarks>
/// Names use a dotted path: a bare name (e.g. <c>"math"</c> or <c>"print"</c>) disables
/// either an entire package or a top-level function/variable, while a dotted name
/// (e.g. <c>"string.dump"</c> or <c>"math.pi"</c>) disables a single member of a package.
/// </remarks>
public sealed class StdlibSection
{
    /// <summary>
    /// Whether to load the embedded standard Lua library declarations at all. When false,
    /// no <c>print</c>, <c>string</c>, <c>math</c>, etc. are visible to the type checker
    /// and consumers must provide their own globals via the <c>globals</c> setting.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Names to remove from the standard library before binding. See the type-level
    /// remarks for the accepted formats.
    /// </summary>
    public List<string> Disabled { get; set; } = [];

    internal void Merge(StdlibSection section)
    {
        Enabled = Config.MergeVal(Enabled, section.Enabled, true);
        Disabled.AddRange(section.Disabled);
    }
}
