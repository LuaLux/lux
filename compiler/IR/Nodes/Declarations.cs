using Nebra.Diagnostics;

namespace Nebra.IR;

public sealed class FunctionDecl(
    NodeID id, TextSpan span,
    List<NameRef> namePath, NameRef? methodName,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isAsync = false
) : Decl(id, span)
{
    public List<NameRef> NamePath { get; } = namePath;
    public NameRef? MethodName { get; } = methodName;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsAsync { get; } = isAsync;
    public List<TypeParamDef> TypeParams { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class LocalFunctionDecl(
    NodeID id, TextSpan span,
    NameRef name,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isAsync = false
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsAsync { get; } = isAsync;
    public List<TypeParamDef> TypeParams { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class LocalDecl(
    NodeID id, TextSpan span,
    List<AttribVar> variables, List<Expr> values,
    bool isMutable = false
) : Decl(id, span)
{
    public List<AttribVar> Variables { get; } = variables;
    public List<Expr> Values { get; } = values;
    public bool IsMutable { get; } = isMutable;
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class DeclareFunctionDecl(
    NodeID id, TextSpan span,
    List<NameRef> namePath, NameRef? methodName,
    List<Parameter> parameters, TypeRef? returnType,
    bool isAsync = false
) : Decl(id, span)
{
    public List<NameRef> NamePath { get; } = namePath;
    public NameRef? MethodName { get; } = methodName;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public bool IsAsync { get; } = isAsync;
    public List<TypeParamDef> TypeParams { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class DeclareVariableDecl(
    NodeID id, TextSpan span,
    NameRef name, TypeRef typeAnnotation
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public TypeRef TypeAnnotation { get; } = typeAnnotation;
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class DeclareModuleDecl(
    NodeID id, TextSpan span,
    NameRef moduleName, List<Decl> members
) : Decl(id, span)
{
    public NameRef ModuleName { get; } = moduleName;
    public List<Decl> Members { get; } = members;
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class EnumDecl(
    NodeID id, TextSpan span,
    NameRef name, List<EnumMember> members, bool isDeclare
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public List<EnumMember> Members { get; } = members;
    public bool IsDeclare { get; } = isDeclare;
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class EnumMember(NameRef name, Expr? value, TypeRef? typeAnnotation, TextSpan span)
{
    public NameRef Name { get; } = name;
    public Expr? Value { get; set; } = value;
    public TypeRef? TypeAnnotation { get; } = typeAnnotation;
    public TextSpan Span { get; } = span;
    public List<Annotation> Annotations { get; set; } = [];
    public Nebra.Doc.DocComment? Doc { get; set; }
}

public sealed class ClassDecl(
    NodeID id, TextSpan span,
    NameRef name, NameRef? baseClass,
    List<NameRef> interfaces,
    List<ClassFieldNode> fields,
    List<ClassMethodNode> methods,
    ClassConstructorNode? constructor,
    List<ClassAccessorNode> accessors,
    bool isDeclare = false,
    bool isAbstract = false
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public NameRef? BaseClass { get; } = baseClass;
    public List<NameRef> Interfaces { get; } = interfaces;
    public List<ClassFieldNode> Fields { get; } = fields;
    public List<ClassMethodNode> Methods { get; } = methods;
    public ClassConstructorNode? Constructor { get; } = constructor;
    public List<ClassAccessorNode> Accessors { get; } = accessors;
    public bool IsDeclare { get; } = isDeclare;
    public bool IsAbstract { get; } = isAbstract;
    public List<TypeParamDef> TypeParams { get; set; } = [];
    /// <summary>Type arguments passed to the base class, if any (e.g. <c>extends Foo&lt;number&gt;</c>).</summary>
    public List<TypeArgRef> BaseClassTypeArgs { get; set; } = [];
    /// <summary>Parallel to <see cref="Interfaces"/>: type arguments for each implemented interface.</summary>
    public List<List<TypeArgRef>> InterfaceTypeArgs { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}

/// <summary>
/// An <c>extend Type ... end</c> block that adds methods to an existing type (class,
/// interface, or a built-in like <c>string</c>/<c>number</c>). Extension method calls lower
/// at compile time to a plain function call, so they work on every type — including primitives.
/// </summary>
public sealed class ExtendDecl(
    NodeID id, TextSpan span,
    TypeRef targetType, List<ExtensionMethodNode> methods
) : Decl(id, span)
{
    public TypeRef TargetType { get; } = targetType;
    public List<ExtensionMethodNode> Methods { get; } = methods;
    public List<Annotation> Annotations { get; set; } = [];
}

public sealed class InterfaceDecl(
    NodeID id, TextSpan span,
    NameRef name, List<NameRef> baseInterfaces,
    List<InterfaceFieldNode> fields,
    List<InterfaceMethodNode> methods,
    bool isDeclare = false
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public List<NameRef> BaseInterfaces { get; } = baseInterfaces;
    public List<InterfaceFieldNode> Fields { get; } = fields;
    public List<InterfaceMethodNode> Methods { get; } = methods;
    public bool IsDeclare { get; } = isDeclare;
    public List<TypeParamDef> TypeParams { get; set; } = [];
    /// <summary>Parallel to <see cref="BaseInterfaces"/>: type arguments for each extended interface.</summary>
    public List<List<TypeArgRef>> BaseInterfaceTypeArgs { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}
