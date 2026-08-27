using Nebra.Configuration.Converter;
using Tomlyn.Serialization;

namespace Nebra.Configuration;

/// <summary>
/// The supported Lua versions Nebra can transpile to.
/// </summary>
[TomlConverter(typeof(LuaVersionConverter))]
public enum LuaVersion
{
    Lua51,
    Lua52,
    Lua53,
    Lua54,
    LuaJIT
}