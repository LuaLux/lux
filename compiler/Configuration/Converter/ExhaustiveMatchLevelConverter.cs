using Tomlyn;
using Tomlyn.Serialization;

namespace Nebra.Configuration.Converter;

internal sealed class ExhaustiveMatchLevelConverter : TomlConverter<ExhaustiveMatchLevel>
{
    public override ExhaustiveMatchLevel Read(TomlReader reader)
    {
        var str = reader.GetString()?.ToLowerInvariant()?.Trim();
        reader.Read();
        if (str == null)
            throw new TomlException("Expected a string value for exhaustive_match, got empty string");

        return str switch
        {
            "none" or "off" or "false" => ExhaustiveMatchLevel.None,
            "relaxed" => ExhaustiveMatchLevel.Relaxed,
            "explicit" or "strict" => ExhaustiveMatchLevel.Explicit,
            _ => throw new TomlException(
                $"Invalid exhaustive_match level: '{str}'. Supported levels are: none, relaxed, explicit")
        };
    }

    public override void Write(TomlWriter writer, ExhaustiveMatchLevel value)
    {
        writer.WriteStringValue(value switch
        {
            ExhaustiveMatchLevel.None => "none",
            ExhaustiveMatchLevel.Relaxed => "relaxed",
            ExhaustiveMatchLevel.Explicit => "explicit",
            _ => throw new TomlException($"Invalid exhaustive_match enum value: '{value}'")
        });
    }
}
