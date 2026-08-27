using Nebra.Configuration.Converter;
using Tomlyn.Serialization;

namespace Nebra.Configuration;

/// <summary>
/// Strictness level for exhaustive match checking in if/elseif chains that match on a union or enum
/// type via <c>is</c> checks or enum member equality.
/// </summary>
[TomlConverter(typeof(ExhaustiveMatchLevelConverter))]
public enum ExhaustiveMatchLevel
{
    /// <summary>
    /// Exhaustive matching is not enforced. Any match chain is accepted regardless of coverage.
    /// </summary>
    None,

    /// <summary>
    /// A match chain is considered exhaustive as soon as every variant is covered OR an <c>else</c>
    /// branch is present that acts as a catch-all.
    /// </summary>
    Relaxed,

    /// <summary>
    /// Every variant of the scrutinee must be explicitly covered by its own branch. An <c>else</c>
    /// branch does not satisfy the check — it only serves as a fallback for impossible cases.
    /// </summary>
    Explicit
}
