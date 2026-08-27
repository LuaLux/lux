using Nebra.PackageManager;
using Tomlyn;
using Tomlyn.Serialization;

namespace Nebra.Configuration.Converter;

internal sealed class LinkerKindConverter : TomlConverter<LinkerKind>
{
    public override LinkerKind Read(TomlReader reader)
    {
        var str = reader.GetString()?.ToLowerInvariant()?.Trim();
        reader.Read();
        if (string.IsNullOrEmpty(str))
            throw new TomlException("expected a string value for linker");

        return str switch
        {
            "auto" => LinkerKind.Auto,
            "symlink" => LinkerKind.Symlink,
            "junction" => LinkerKind.Junction,
            "copy" => LinkerKind.Copy,
            _ => throw new TomlException($"invalid linker '{str}'. Expected: auto, symlink, junction, copy")
        };
    }

    public override void Write(TomlWriter writer, LinkerKind value)
    {
        writer.WriteStringValue(value switch
        {
            LinkerKind.Auto => "auto",
            LinkerKind.Symlink => "symlink",
            LinkerKind.Junction => "junction",
            LinkerKind.Copy => "copy",
            _ => throw new TomlException($"invalid linker enum value: '{value}'")
        });
    }
}
