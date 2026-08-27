using Nebra.Configuration.Converter;
using Tomlyn.Serialization;

namespace Nebra.Configuration;

/// <summary>
/// Controls how much runtime reflection metadata the compiler emits into the output.
/// </summary>
[TomlConverter(typeof(ReflectionModeConverter))]
public enum ReflectionMode
{
    /// <summary>No metadata is emitted; zero overhead. <c>reflect</c> queries find nothing.</summary>
    None,

    /// <summary>Only declarations marked with <c>@reflectable</c> are described.</summary>
    Annotated,

    /// <summary>Every class, interface, enum, function and module variable is described.</summary>
    All
}
