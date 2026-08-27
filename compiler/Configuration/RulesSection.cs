namespace Nebra.Configuration;

/// <summary>
/// The [rules] section of the <see cref="Config"/>. The rules section is used to configure the rules of the compiler,
/// such as whether to allow any type, whether to enable strict nil checking, and other rules that can affect the code
/// quality and maintainability of the generated Lua code.
/// </summary>
public sealed class RulesSection
{
    /// <summary>
    /// Whether to allow any type. If true, the compiler will not report any errors for any type. This is useful for
    /// quick prototyping and for gradually adding types to an existing codebase. However, it is not recommended for
    /// production code, as it can lead to poor code quality and maintainability.
    /// </summary>
    public bool AllowAny { get; set; } = true;
    
    /// <summary>
    /// Whether to enable strict nil checking. If true, the compiler will report an error if a variable is assigned nil
    /// without being marked as nilable by the ? operator.
    /// </summary>
    public bool StrictNil { get; set; } = false;
    
    /// <summary>
    /// Whether to apply immutable by default. This will make all variables immutable by default, unless they are
    /// explicitly marked as mutable by the <c>mutable</c> keyword. This is useful for ensuring immutability and
    /// preventing accidental mutations, which can lead to safer and more maintainable code. However, it can also be
    /// inconvenient for quick prototyping and for codebases that heavily rely on mutable state, so it is not enabled
    /// by default.
    /// </summary>
    public bool ImmutableDefault { get; set; } = false;

    /// <summary>
    /// When enabled in combination with <see cref="ImmutableDefault"/>, it will apply a deep immutability to all
    /// variables, meaning that not only the variable itself is immutable, but also any data it references is immutable.
    /// This can be useful for ensuring deep immutability and preventing accidental mutations of referenced data, which
    /// can lead to safer and more maintainable code. However, it can also be inconvenient for quick prototyping and for
    /// codebases that heavily rely on mutable state, so it is not enabled by default.
    /// </summary>
    /// <remarks>
    /// <b>Important</b>: Deep freeze will be enforced by meta-functions on the table level which can add little overhead
    /// to table operations. Therefore, it is recommended to only enable deep freeze for codebases that require strict
    /// immutability and do not heavily rely on mutable state, such as libraries or frameworks that are designed to be
    /// used by other codebases. For codebases that heavily rely on mutable state, it is recommended to only enable
    /// <see cref="ImmutableDefault"/> without deep freeze.
    /// </remarks>
    public bool DeepFreeze { get; set; } = false;
    
    /// <summary>
    /// Strictness level for exhaustive matching in if/elseif chains that match on a union or enum
    /// type via <c>is</c> checks or enum member equality. See <see cref="ExhaustiveMatchLevel"/> for
    /// the available levels. <see cref="ExhaustiveMatchLevel.None"/> disables the check entirely,
    /// <see cref="ExhaustiveMatchLevel.Relaxed"/> accepts an <c>else</c> branch as a catch-all, and
    /// <see cref="ExhaustiveMatchLevel.Explicit"/> requires every variant to be covered by its own
    /// branch regardless of <c>else</c>.
    /// </summary>
    public ExhaustiveMatchLevel ExhaustiveMatch { get; set; } = ExhaustiveMatchLevel.None;

    internal void Merge(RulesSection section)
    {
        AllowAny = Config.MergeVal(AllowAny, section.AllowAny, false);
        StrictNil = Config.MergeVal(StrictNil, section.StrictNil, false);
        ImmutableDefault = Config.MergeVal(ImmutableDefault, section.ImmutableDefault, false);
        DeepFreeze = Config.MergeVal(DeepFreeze, section.DeepFreeze, false);
        ExhaustiveMatch = Config.MergeVal(ExhaustiveMatch, section.ExhaustiveMatch, ExhaustiveMatchLevel.None);
    }

    internal void ApplyStrictPreset()
    {
        AllowAny = Config.MergeVal(AllowAny, true, false);
        StrictNil = Config.MergeVal(StrictNil, true, false);
        ImmutableDefault = Config.MergeVal(ImmutableDefault, true, false);
        ExhaustiveMatch = Config.MergeVal(ExhaustiveMatch, ExhaustiveMatchLevel.Explicit, ExhaustiveMatchLevel.None);
    }
}