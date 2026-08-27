using Nebra.Diagnostics;

namespace Nebra.IR;

public sealed class NilLiteralExpr(NodeID id, TextSpan span) : Expr(id, span);

public sealed class BoolLiteralExpr(NodeID id, TextSpan span, bool value) : Expr(id, span)
{
    public bool Value { get; } = value;
}

public sealed class NumberLiteralExpr(NodeID id, TextSpan span, string raw, NumberKind kind) : Expr(id, span)
{
    public string Raw { get; } = raw;
    public NumberKind Kind { get; } = kind;
}

public sealed class StringLiteralExpr(NodeID id, TextSpan span, string value) : Expr(id, span)
{
    public string Value { get; } = value;
}

public sealed class InterpolatedStringExpr(NodeID id, TextSpan span, List<InterpStringPart> parts) : Expr(id, span)
{
    public List<InterpStringPart> Parts { get; } = parts;
}

public abstract class InterpStringPart(TextSpan span)
{
    public TextSpan Span { get; } = span;
}

public sealed class InterpTextPart(TextSpan span, string text) : InterpStringPart(span)
{
    public string Text { get; } = text;
}

public sealed class InterpExprPart(TextSpan span, Expr expression) : InterpStringPart(span)
{
    public Expr Expression { get; } = expression;
}

public sealed class VarargExpr(NodeID id, TextSpan span) : Expr(id, span);

public sealed class FunctionDefExpr(
    NodeID id, TextSpan span,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isAsync = false
) : Expr(id, span)
{
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsAsync { get; } = isAsync;
    public List<TypeParamDef> TypeParams { get; set; } = [];
}

public sealed class BinaryExpr(NodeID id, TextSpan span, BinaryOp op, Expr left, Expr right) : Expr(id, span)
{
    public BinaryOp Op { get; } = op;
    public Expr Left { get; } = left;
    public Expr Right { get; } = right;
}

public sealed class UnaryExpr(NodeID id, TextSpan span, UnaryOp op, Expr operand) : Expr(id, span)
{
    public UnaryOp Op { get; } = op;
    public Expr Operand { get; } = operand;
}

public sealed class NameExpr(NodeID id, TextSpan span, NameRef name) : Expr(id, span)
{
    public NameRef Name { get; } = name;
}

public sealed class ParenExpr(NodeID id, TextSpan span, Expr inner) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
}

public sealed class DotAccessExpr(NodeID id, TextSpan span, Expr @object, NameRef fieldName, bool isOptional = false) : Expr(id, span)
{
    public Expr Object { get; } = @object;
    public NameRef FieldName { get; } = fieldName;
    public bool IsOptional { get; } = isOptional;
}

public sealed class IndexAccessExpr(NodeID id, TextSpan span, Expr @object, Expr index) : Expr(id, span)
{
    public Expr Object { get; } = @object;
    public Expr Index { get; } = index;
}

public sealed class FunctionCallExpr(NodeID id, TextSpan span, Expr callee, List<Expr> arguments, bool isOptional = false) : Expr(id, span)
{
    public Expr Callee { get; } = callee;
    public List<Expr> Arguments { get; } = arguments;
    public bool IsOptional { get; } = isOptional;
}

public sealed class MethodCallExpr(NodeID id, TextSpan span, Expr @object, NameRef methodName, List<Expr> arguments) : Expr(id, span)
{
    public Expr Object { get; } = @object;
    public NameRef MethodName { get; } = methodName;
    public List<Expr> Arguments { get; } = arguments;

    /// <summary>
    /// Set by type inference when this call resolves to an extension method: the type the
    /// extension was declared on. Codegen lowers the call to a plain function invocation
    /// <c>fn(receiver, args...)</c> instead of a Lua <c>:</c> method call.
    /// </summary>
    public TypID? ExtensionTargetType { get; set; }
}

public sealed class NonNilAssertExpr(NodeID id, TextSpan span, Expr inner) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
}

public sealed class IncDecExpr(NodeID id, TextSpan span, Expr target, bool isPre, bool isIncrement) : Expr(id, span)
{
    public Expr Target { get; } = target;
    public bool IsPre { get; } = isPre;
    public bool IsIncrement { get; } = isIncrement;
}

public sealed class TypeCheckExpr(NodeID id, TextSpan span, Expr inner, TypeRef targetType) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
    public TypeRef TargetType { get; } = targetType;
}

public sealed class TypeCastExpr(NodeID id, TextSpan span, Expr inner, TypeRef targetType) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
    public TypeRef TargetType { get; } = targetType;
}

public sealed class TypeOfExpr(NodeID id, TextSpan span, Expr inner) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
}

public sealed class InstanceOfExpr(NodeID id, TextSpan span, Expr inner, NameRef className) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
    public NameRef ClassName { get; } = className;
    /// <summary>
    /// The full type reference from the source, including any generic type arguments
    /// (e.g. <c>List&lt;number&gt;</c>). Null if the instanceof target was a bare name.
    /// Generic arguments are validated at compile time and erased at runtime.
    /// </summary>
    public TypeRef? TargetType { get; set; }
}

public sealed class AwaitExpr(NodeID id, TextSpan span, Expr expression) : Expr(id, span)
{
    public Expr Expression { get; } = expression;
}

public sealed class TableConstructorExpr(NodeID id, TextSpan span, List<TableField> fields) : Expr(id, span)
{
    public List<TableField> Fields { get; } = fields;
}

public sealed class MatchExpr(NodeID id, TextSpan span, Expr scrutinee, List<MatchExprArm> arms) : Expr(id, span)
{
    public Expr Scrutinee { get; } = scrutinee;
    public List<MatchExprArm> Arms { get; } = arms;
}

public sealed class NewExpr(NodeID id, TextSpan span, NameRef className, List<Expr> arguments) : Expr(id, span)
{
    public NameRef ClassName { get; } = className;
    public List<Expr> Arguments { get; } = arguments;
}

public sealed class SuperCallExpr(NodeID id, TextSpan span, List<Expr> arguments) : Expr(id, span)
{
    public List<Expr> Arguments { get; } = arguments;
}
