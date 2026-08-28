using System.Text;
using Nebra.Configuration;

namespace Nebra.Compiler.Codegen;

public sealed class LuaGenerator(Config config)
{
    private readonly StringBuilder _preamble = new();
    private readonly StringBuilder _body = new();
    private readonly HashSet<string> _emittedHelpers = [];
    private readonly Dictionary<string, string> _helperNames = new();
    private int _indent;
    private bool _lineStart = true;
    private int _tempCounter;

    public bool Minify => config.Minify;
    public LuaVersion Target => config.Target;
    public LuaFeatureSet Features { get; } = LuaFeatureSet.For(config.Target);

    public Config Config => config;

    public string Finish()
    {
        var sb = new StringBuilder();
        if (_preamble.Length > 0)
        {
            sb.Append(_preamble);
            if (!Minify)
                sb.AppendLine();
        }
        sb.Append(_body);
        return sb.ToString();
    }

    #region Writing

    public void Write(string text)
    {
        if (_lineStart && !Minify)
        {
            _body.Append(new string('\t', _indent));
            _lineStart = false;
        }
        _body.Append(text);
    }

    public void WriteLine(string text)
    {
        Write(text);
        NewLine();
    }

    public void WriteLine()
    {
        NewLine();
    }

    public void NewLine()
    {
        if (Minify)
        {
            _body.Append(' ');
        }
        else
        {
            _body.AppendLine();
        }
        _lineStart = true;
    }

    public void Space()
    {
        if (!Minify)
            _body.Append(' ');
    }

    public void Sep()
    {
        if (Minify)
            _body.Append(' ');
        else
            _body.Append(' ');
    }

    public void WriteSemicolon()
    {
        if (config.Code.Semicolons == Policy.Required)
            _body.Append(';');
    }

    public void Indent() => _indent++;
    public void Dedent() => _indent = Math.Max(0, _indent - 1);

    #endregion

    #region Preamble / Helpers

    public void WritePreambleLine(string text)
    {
        _preamble.AppendLine(text);
    }

    public string RequireHelper(string key, Func<string, string> helperFactory)
    {
        if (_helperNames.TryGetValue(key, out var existing))
            return existing;

        var name = "__nebra_" + key;
        _helperNames[key] = name;

        if (_emittedHelpers.Add(key))
        {
            var code = helperFactory(name);
            _preamble.AppendLine(code);
        }

        return name;
    }

    #endregion

    #region Hoisting

    private readonly List<string> _hoisted = [];

    public void Hoist(string luaCode)
    {
        _hoisted.Add(luaCode);
    }

    public void FlushHoisted()
    {
        foreach (var code in _hoisted)
        {
            WriteLine(code);
        }
        _hoisted.Clear();
    }

    public bool HasHoisted => _hoisted.Count > 0;

    #endregion

    #region Temp Vars

    public string FreshTemp(string prefix = "_t")
    {
        return prefix + _tempCounter++;
    }

    #endregion

    #region Comments

    public void WriteComment(string text)
    {
        if (Minify) return;
        WriteLine("-- " + text);
    }

    public void WriteBlockComment(string text)
    {
        if (Minify) return;
        WriteLine("--[[ " + text + " ]]");
    }

    #endregion

    #region Concat Helpers

    public string GetConcatHelper()
    {
        return RequireHelper("concat", name =>
            $"local function {name}(a, b) return tostring(a) .. tostring(b) end");
    }

    #endregion
    
    #region Freeze Helpers
    
    public string GetFreezeHelper()
    {
        return RequireHelper("freeze", name =>
            $"local function {name}(t) return setmetatable(t, {{__newindex = function() error(\"attempt to modify frozen table\") end}}) end");
    }
    
    #endregion

    #region Index Base Helpers

    public int IndexBase => config.Code.IndexBase;

    public string AdjustIndex(string indexExpr)
    {
        var offset = config.Code.IndexBase;
        if (offset == 1) return indexExpr;

        var diff = 1 - offset;
        if (diff > 0)
            return "(" + indexExpr + " + " + diff + ")";
        return "(" + indexExpr + " - " + (-diff) + ")";
    }

    public string GetIpairsHelper()
    {
        if (config.Code.IndexBase == 1) return "ipairs";

        var adj = 1 - config.Code.IndexBase;
        return RequireHelper("ipairs", name =>
            $"local function {name}(t) local i = {config.Code.IndexBase - 1} return function() i = i + 1 local v = t[i + {adj}] if v == nil then return nil end return i, v end end");
    }

    #endregion

    #region Increment / Decrement Helpers

    public string GetIncDecHelper(bool isPre, bool isIncrement)
    {
        var key = (isPre ? "pre" : "post") + (isIncrement ? "inc" : "dec");
        var op = isIncrement ? "+" : "-";
        if (isPre)
        {
            return RequireHelper(key, name =>
                $"local function {name}(get, set) local v = get() {op} 1 set(v) return v end");
        }
        else
        {
            return RequireHelper(key, name =>
                $"local function {name}(get, set) local v = get() set(v {op} 1) return v end");
        }
    }

    #endregion

    #region Import Helpers

    public string EmitImport(string modulePath)
    {
        if (modulePath.EndsWith(".neb", StringComparison.OrdinalIgnoreCase))
            modulePath = modulePath[..^4];
        modulePath += config.Code.ImportExtension;
        var template = config.Code.ImportStatement;
        return template.Replace("%s", "\"" + modulePath + "\"");
    }

    #endregion

    #region Operator Mapping

    public string BinaryOpToLua(IR.BinaryOp op)
    {
        return op switch
        {
            IR.BinaryOp.Add => "+",
            IR.BinaryOp.Sub => "-",
            IR.BinaryOp.Mul => "*",
            IR.BinaryOp.Div => "/",
            IR.BinaryOp.FloorDiv => Features.HasFloorDiv ? "//" : "math.floor(a/b)",
            IR.BinaryOp.Mod => "%",
            IR.BinaryOp.Pow => "^",
            IR.BinaryOp.Concat => "..",
            IR.BinaryOp.Eq => "==",
            IR.BinaryOp.Neq => "~=",
            IR.BinaryOp.Lt => "<",
            IR.BinaryOp.Gt => ">",
            IR.BinaryOp.Lte => "<=",
            IR.BinaryOp.Gte => ">=",
            IR.BinaryOp.And => "and",
            IR.BinaryOp.Or => "or",
            IR.BinaryOp.BitwiseAnd => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => "&",
                BitwiseStyle.BitLib => "bit.band",
                _ => ""
            },
            IR.BinaryOp.BitwiseOr => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => "|",
                BitwiseStyle.BitLib => "bit.bor",
                _ => ""
            },
            IR.BinaryOp.BitwiseXor => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => "~",
                BitwiseStyle.BitLib => "bit.bxor",
                _ => ""
            },
            IR.BinaryOp.LShift => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => "<<",
                BitwiseStyle.BitLib => "bit.lshift",
                _ => ""
            },
            IR.BinaryOp.RShift => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => ">>",
                BitwiseStyle.BitLib => "bit.rshift",
                _ => ""
            },
            _ => "?"
        };
    }

    public string UnaryOpToLua(IR.UnaryOp op)
    {
        return op switch
        {
            IR.UnaryOp.Negate => "-",
            IR.UnaryOp.LogicalNot => "not ",
            IR.UnaryOp.Length => "#",
            IR.UnaryOp.BitwiseNot => Features.BitwiseStyle switch
            {
                BitwiseStyle.Operator => "~",
                BitwiseStyle.BitLib => "bit.bnot",
                _ => ""
            },
            _ => "?"
        };
    }

    public bool IsBinaryInfix(IR.BinaryOp op)
    {
        if (!Features.HasBitwise && IsBitwiseOp(op)) return false;
        if (op == IR.BinaryOp.FloorDiv && !Features.HasFloorDiv) return false;
        return Features.BitwiseStyle != BitwiseStyle.BitLib || !IsBitwiseOp(op);
    }

    public bool IsBitwiseOp(IR.BinaryOp op) =>
        op is IR.BinaryOp.BitwiseAnd or IR.BinaryOp.BitwiseOr or IR.BinaryOp.BitwiseXor
            or IR.BinaryOp.LShift or IR.BinaryOp.RShift;

    public bool IsConfiguredConcatOp(IR.BinaryOp op)
    {
        var configured = config.Code.ConcatOperator;
        if (string.IsNullOrEmpty(configured)) return false;
        var mapped = configured switch
        {
            "+" => IR.BinaryOp.Add,
            "-" => IR.BinaryOp.Sub,
            "*" => IR.BinaryOp.Mul,
            "/" => IR.BinaryOp.Div,
            "//" => IR.BinaryOp.FloorDiv,
            "%" => IR.BinaryOp.Mod,
            "^" => IR.BinaryOp.Pow,
            ".." => IR.BinaryOp.Concat,
            _ => (IR.BinaryOp?)null
        };
        return mapped.HasValue && mapped.Value == op;
    }

    #endregion

    #region Async Driver Helper

    public string GetAsyncDriverHelper()
    {
        var unpackFn = Features.HasTableUnpack ? "table.unpack" : "unpack";
        return RequireHelper("async_drive", name =>
            $"local function {name}(co, done) " +
            $"local function step(...) " +
            $"local ok, payload = coroutine.resume(co, ...) " +
            $"if not ok then error(payload) end " +
            $"if coroutine.status(co) == \"dead\" then if done then done(payload) end return end " +
            $"local fn = payload[1] local n = payload.n " +
            $"local args = {{{unpackFn}(payload, 2, n)}} " +
            $"args[#args + 1] = step " +
            $"fn({unpackFn}(args)) " +
            $"end step() end");
    }

    #endregion

    #region Class Proxy Helper

    public string GetClassProxyHelper()
    {
        return RequireHelper("class_proxy", name =>
            $"local function {name}(cls, parent) " +
            $"local mt = {{ " +
            $"__index = function(t, k) " +
            $"local g = cls[\"__get_\" .. k] " +
            $"if g then return g(t) end " +
            $"local v = cls[k] " +
            $"if v ~= nil then return v end " +
            $"if parent then " +
            $"g = parent[\"__get_\" .. k] " +
            $"if g then return g(t) end " +
            $"return parent[k] " +
            $"end " +
            $"end, " +
            $"__newindex = function(t, k, v) " +
            $"local s = cls[\"__set_\" .. k] " +
            $"if s then s(t, v) return end " +
            $"if cls[\"__get_\" .. k] then error(\"Cannot set readonly property '\" .. k .. \"'\") end " +
            $"if parent then " +
            $"s = parent[\"__set_\" .. k] " +
            $"if s then s(t, v) return end " +
            $"if parent[\"__get_\" .. k] then error(\"Cannot set readonly property '\" .. k .. \"'\") end " +
            $"end " +
            $"rawset(t, k, v) " +
            $"end " +
            $"}} " +
            $"local __ops = {{\"__add\",\"__sub\",\"__mul\",\"__div\",\"__mod\",\"__pow\",\"__unm\",\"__concat\",\"__len\",\"__eq\",\"__lt\",\"__le\",\"__idiv\"}} " +
            $"for i = 1, #__ops do " +
            $"local k = __ops[i] " +
            $"if rawget(cls, k) then mt[k] = cls[k] " +
            $"elseif parent and rawget(parent, k) then mt[k] = parent[k] end " +
            $"end " +
            $"return mt end");
    }

    #endregion

    #region TypeOf / InstanceOf Helpers

    public string GetTypeOfHelper()
    {
        return RequireHelper("typeof", name =>
            $"local function {name}(v) " +
            $"local t = type(v) " +
            $"if t == \"table\" then " +
            $"local mt = getmetatable(v) " +
            $"while mt ~= nil do " +
            $"if type(mt) == \"table\" and type(mt.__index) == \"table\" and mt.__index.__name then return mt.__index.__name end " +
            $"if type(mt) == \"table\" and mt.__name then return mt.__name end " +
            $"mt = getmetatable(mt) " +
            $"end " +
            $"end " +
            $"return t " +
            $"end");
    }

    /// <summary>
    /// Emits the helper backing an `is` test against an interface. Interfaces have no runtime
    /// value, so the check looks up the name in the `__interfaces` set the class carries.
    /// </summary>
    public string GetImplementsHelper()
    {
        return RequireHelper("implements", name =>
            $"local function {name}(v, iface) " +
            $"if type(v) ~= \"table\" then return false end " +
            $"local mt = getmetatable(v) " +
            $"while type(mt) == \"table\" do " +
            $"if type(mt.__interfaces) == \"table\" and mt.__interfaces[iface] then return true end " +
            $"if type(mt.__index) == \"table\" then " +
            $"if type(mt.__index.__interfaces) == \"table\" and mt.__index.__interfaces[iface] then return true end " +
            $"mt = getmetatable(mt.__index) " +
            $"else " +
            $"mt = getmetatable(mt) " +
            $"end " +
            $"end " +
            $"return false " +
            $"end");
    }

    /// <summary>
    /// Emits the alias both spellings of unpack are rewritten to. Lua 5.1 and LuaJIT provide only
    /// the global <c>unpack</c>, Lua 5.2 and later only <c>table.unpack</c>. The alias is bound at
    /// the top of the chunk, so it cannot be captured by a local the source declares later.
    /// </summary>
    public string GetUnpackHelper()
    {
        return RequireHelper("unpack", name => $"local {name} = table.unpack or unpack");
    }

    public string GetInstanceOfHelper()
    {
        return RequireHelper("instanceof", name =>
            $"local function {name}(v, cls) " +
            $"if type(v) ~= \"table\" or cls == nil then return false end " +
            $"local mt = getmetatable(v) " +
            $"while mt ~= nil do " +
            $"if mt == cls then return true end " +
            $"if type(mt) == \"table\" and mt.__index == cls then return true end " +
            $"if type(mt) == \"table\" and type(mt.__index) == \"table\" then " +
            $"if mt.__index == cls then return true end " +
            $"mt = getmetatable(mt.__index) " +
            $"else " +
            $"mt = getmetatable(mt) " +
            $"end " +
            $"end " +
            $"return false " +
            $"end");
    }

    #endregion

    #region Floor Div Helper

    public string GetFloorDivHelper()
    {
        return RequireHelper("floordiv", name =>
            $"local function {name}(a, b) return math.floor(a / b) end");
    }

    #endregion

    #region Scope Blocks

    public void BeginBlock(string header)
    {
        WriteLine(header);
        Indent();
    }

    public void EndBlock(string footer = "end")
    {
        Dedent();
        WriteLine(footer);
    }

    #endregion
}
