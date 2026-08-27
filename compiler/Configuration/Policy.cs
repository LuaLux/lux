using Nebra.Configuration.Converter;
using Tomlyn.Serialization;

namespace Nebra.Configuration;

/// <summary>
/// The policy for a specific configuration option. This is used to determine how user can do or not do specific things.
/// </summary>
[TomlConverter(typeof(PolicyConverter))]
public enum Policy
{
    Optional,
    Required,
    Forbidden
}