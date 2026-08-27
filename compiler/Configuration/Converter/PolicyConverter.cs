using Tomlyn;
using Tomlyn.Serialization;

namespace Nebra.Configuration.Converter;

internal sealed class PolicyConverter : TomlConverter<Policy>
{
    public override Policy Read(TomlReader reader)
    {
        var str = reader.GetString()?.ToLowerInvariant()?.Trim();
        reader.Read();
        if (str == null)
            throw new TomlException("Expected a string value for policy, got empty string");

        return str switch
        {
            "optional" => Policy.Optional,
            "required" => Policy.Required,
            "forbidden" => Policy.Forbidden,
            _ => throw new TomlException($"Invalid policy string: '{str}'. Supported policies are: optional, required, forbidden")
        };
    }

    public override void Write(TomlWriter writer, Policy value)
    {
        writer.WriteStringValue(value switch
        {
            Policy.Optional => "optional",
            Policy.Required => "required",
            Policy.Forbidden => "forbidden",
            _ => throw new TomlException($"Invalid policy enum value: '{value}'. Supported policies are: optional, required, forbidden")
        });
    }
}