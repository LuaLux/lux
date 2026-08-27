-- IR helper factories for annotation apply() scripts.
-- Auto-loaded into the sandboxed NebraRuntime by ApplyAnnotationsPass.
-- Returned tables use the same wire format that IRLuaCodec expects when decoding.
-- Discriminator field is "__kind" (chosen to avoid collisions with IR properties
-- that happen to be named "Kind", e.g. on TypeRef).

ir = {}

local function span()
    return { 1, 1, 1, 1 }
end

local function nameRef(name)
    return { name = name, span = span() }
end

function ir.stringLiteral(value)
    return { __kind = "StringLiteralExpr", __span = span(), value = value }
end

function ir.numberLiteral(raw)
    return { __kind = "NumberLiteralExpr", __span = span(), raw = tostring(raw), kind = "Int" }
end

function ir.boolLiteral(value)
    return { __kind = "BoolLiteralExpr", __span = span(), value = value and true or false }
end

function ir.nilLiteral()
    return { __kind = "NilLiteralExpr", __span = span() }
end

function ir.nameExpr(name)
    return { __kind = "NameExpr", __span = span(), name = nameRef(name) }
end

function ir.call(callee, args)
    local calleeNode = type(callee) == "string" and ir.nameExpr(callee) or callee
    return {
        __kind = "FunctionCallExpr",
        __span = span(),
        callee = calleeNode,
        arguments = args or {},
        isOptional = false,
    }
end

function ir.methodCall(object, method, args)
    return {
        __kind = "MethodCallExpr",
        __span = span(),
        object = object,
        methodName = nameRef(method),
        arguments = args or {},
    }
end

function ir.exprStmt(expr)
    return { __kind = "ExprStmt", __span = span(), expression = expr }
end

function ir.returnStmt(values)
    return { __kind = "ReturnStmt", __span = span(), values = values or {} }
end

function ir.dotAccess(object, field)
    return {
        __kind = "DotAccessExpr",
        __span = span(),
        object = object,
        fieldName = nameRef(field),
        isOptional = false,
    }
end

function ir.newExpr(className, args)
    return {
        __kind = "NewExpr",
        __span = span(),
        className = nameRef(className),
        arguments = args or {},
    }
end

function ir.localDecl(name, value)
    return {
        __kind = "LocalDecl",
        __span = span(),
        variables = { { name = nameRef(name), attribute = nil, typeAnnotation = nil, span = span() } },
        values = value and { value } or {},
        isMutable = true,
    }
end

function ir.varargExpr()
    return { __kind = "VarargExpr", __span = span() }
end

-- Builds an Annotation IR node. `name` is the annotation ident (e.g. "RemoteEvent").
-- `args` is a list of either {name = "k", value = exprIR} or just exprIR (positional).
function ir.annotation(name, args)
    local out = {}
    for _, a in ipairs(args or {}) do
        if a.__kind ~= nil then
            -- Bare expr → positional arg
            out[#out + 1] = { name = nil, value = a, span = span() }
        else
            out[#out + 1] = { name = a.name, value = a.value, span = span() }
        end
    end
    return {
        __kind = "Annotation",
        __span = span(),
        name = nameRef(name),
        args = out,
    }
end

-- Parameter helper. `name` becomes the parameter ident; `isVararg = true`
-- produces a `...` parameter (in which case `name` is conventionally "vararg"
-- or "args" and only used for diagnostics).
function ir.param(name, opts)
    opts = opts or {}
    return {
        __kind = "Parameter",
        __span = span(),
        name = nameRef(name or "_"),
        typeAnnotation = nil,
        isVararg = opts.isVararg and true or false,
        defaultValue = nil,
    }
end

-- Anonymous `function(...) ... end`. `params` is a list of ir.param()s
-- (defaults to a single `...` param). `body` is a list of stmts; `returnStmt`
-- is optional.
function ir.functionDef(params, body, returnStmt)
    return {
        __kind = "FunctionDefExpr",
        __span = span(),
        parameters = params or { ir.param("vararg", { isVararg = true }) },
        returnType = nil,
        body = body or {},
        returnStmt = returnStmt,
        isAsync = false,
    }
end

-- Convenience: `function(...) inst:Method(...) end` — the closure shape
-- @nanosPackage emits for every Subscribe call. `instExpr` is an Expr (e.g.
-- ir.nameExpr("__inst")); `methodName` is the bare method ident.
function ir.varargMethodCallClosure(instExpr, methodName)
    return ir.functionDef(
        { ir.param("vararg", { isVararg = true }) },
        { ir.exprStmt(ir.methodCall(instExpr, methodName, { ir.varargExpr() })) },
        nil
    )
end
