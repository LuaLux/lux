using Tomlyn;
using Tomlyn.Serialization;

namespace Nebra.Configuration.Converter;

internal sealed class LuaVersionConverter : TomlConverter<LuaVersion>
{
    private static readonly List<Tuple<LuaVersion, string>> LuaVersionStrings =
    [
        Tuple.Create(LuaVersion.Lua51, "5.1"),
        Tuple.Create(LuaVersion.Lua52, "5.2"),
        Tuple.Create(LuaVersion.Lua53, "5.3"),
        Tuple.Create(LuaVersion.Lua54, "5.4"),
        Tuple.Create(LuaVersion.LuaJIT, "jit")
    ];
    
    private static readonly Dictionary<LuaVersion, string> LuaVersionToString = LuaVersionStrings.ToDictionary(t => t.Item1, t => t.Item2);
    private static readonly Dictionary<string, LuaVersion> StringToLuaVersion = LuaVersionStrings.ToDictionary(t => t.Item2, t => t.Item1);
    
    public override LuaVersion Read(TomlReader reader)
    {
        var str = reader.GetString()?.ToLowerInvariant()?.Trim();
        reader.Read();
        if (str == null)
            throw new TomlException("Expected a string value for Lua version, got empty string");

        if (!StringToLuaVersion.TryGetValue(str, out var luaVersion))
            throw new TomlException($"Invalid Lua version string: '{str}'. Supported versions are: {string.Join(", ", StringToLuaVersion.Keys)}");

        return luaVersion;
    }

    public override void Write(TomlWriter writer, LuaVersion value)
    {
        if (!LuaVersionToString.TryGetValue(value, out var str))
            throw new TomlException($"Invalid Lua version enum value: '{value}'. Supported versions are: {string.Join(", ", LuaVersionToString.Keys)}");

        writer.WriteStringValue(str);
    }
}