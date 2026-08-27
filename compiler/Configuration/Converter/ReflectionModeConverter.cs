using Tomlyn;
using Tomlyn.Serialization;

namespace Nebra.Configuration.Converter;

internal sealed class ReflectionModeConverter : TomlConverter<ReflectionMode>
{
    public override ReflectionMode Read(TomlReader reader)
    {
        var str = reader.GetString()?.ToLowerInvariant()?.Trim();
        reader.Read();

        return str switch
        {
            "none" or "off" or "false" => ReflectionMode.None,
            "annotated" => ReflectionMode.Annotated,
            "all" or "full" or "true" => ReflectionMode.All,
            _ => throw new TomlException(
                $"Invalid reflection mode: '{str}'. Supported modes are: none, annotated, all")
        };
    }

    public override void Write(TomlWriter writer, ReflectionMode value)
    {
        writer.WriteStringValue(value switch
        {
            ReflectionMode.None => "none",
            ReflectionMode.Annotated => "annotated",
            ReflectionMode.All => "all",
            _ => throw new TomlException($"Invalid reflection mode value: '{value}'")
        });
    }
}
