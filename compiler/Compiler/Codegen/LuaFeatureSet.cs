using Nebra.Configuration;

namespace Nebra.Compiler.Codegen;

public enum BitwiseStyle
{
    None,
    Operator,
    BitLib
}

public sealed class LuaFeatureSet
{
    public bool HasGoto { get; init; }
    public bool HasFloorDiv { get; init; }
    public bool HasBitwise { get; init; }
    public BitwiseStyle BitwiseStyle { get; init; }
    public bool HasIntegerType { get; init; }
    public bool HasContinue { get; init; }
    public bool HasConstLocal { get; init; }
    public bool HasCloseLocal { get; init; }
    public bool HasTableUnpack { get; init; }

    public static LuaFeatureSet For(LuaVersion version)
    {
        return version switch
        {
            LuaVersion.Lua51 => new LuaFeatureSet
            {
                HasGoto = false,
                HasFloorDiv = false,
                HasBitwise = false,
                BitwiseStyle = BitwiseStyle.None,
                HasIntegerType = false,
                HasContinue = false,
                HasConstLocal = false,
                HasCloseLocal = false,
                HasTableUnpack = false,
            },
            LuaVersion.Lua52 => new LuaFeatureSet
            {
                HasGoto = true,
                HasFloorDiv = false,
                HasBitwise = false,
                BitwiseStyle = BitwiseStyle.None,
                HasIntegerType = false,
                HasContinue = false,
                HasConstLocal = false,
                HasCloseLocal = false,
                HasTableUnpack = true,
            },
            LuaVersion.Lua53 => new LuaFeatureSet
            {
                HasGoto = true,
                HasFloorDiv = true,
                HasBitwise = true,
                BitwiseStyle = BitwiseStyle.Operator,
                HasIntegerType = true,
                HasContinue = false,
                HasConstLocal = false,
                HasCloseLocal = false,
                HasTableUnpack = true,
            },
            LuaVersion.Lua54 => new LuaFeatureSet
            {
                HasGoto = true,
                HasFloorDiv = true,
                HasBitwise = true,
                BitwiseStyle = BitwiseStyle.Operator,
                HasIntegerType = true,
                HasContinue = false,
                HasConstLocal = true,
                HasCloseLocal = true,
                HasTableUnpack = true,
            },
            LuaVersion.LuaJIT => new LuaFeatureSet
            {
                HasGoto = true,
                HasFloorDiv = false,
                HasBitwise = true,
                BitwiseStyle = BitwiseStyle.BitLib,
                HasIntegerType = false,
                HasContinue = false,
                HasConstLocal = false,
                HasCloseLocal = false,
                HasTableUnpack = false,
            },
            _ => For(LuaVersion.Lua54)
        };
    }
}
