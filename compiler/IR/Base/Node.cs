using Nebra.Diagnostics;

namespace Nebra.IR;

/// <summary>
/// Represents a node in the intermediate representation (IR) of the program. Each node has a unique ID and a text span
/// that indicates where in the source code the node corresponds to.
/// </summary>
public abstract class Node(NodeID id, TextSpan span)
{
    /// <summary>
    /// The unique ID of the node. This is assigned when the node is created and can be used to reference the node from other nodes or from external code.
    /// </summary>
    public NodeID ID { get; } = id;
    
    /// <summary>
    /// The text span of the node. This indicates where in the source code the node corresponds to, and can be used for error reporting and other diagnostics.
    /// </summary>
    public TextSpan Span { get; } = span;
    
    /// <summary>
    /// An implicit conversion operator that allows a Node to be implicitly converted to its NodeID.
    /// </summary>
    public static implicit operator NodeID(Node node) => node.ID;
}

/// <summary>
/// Represents an expression in the intermediate representation (IR) of the program. An expression is a node that
/// produces a value, and can be used in other expressions or statements. Each expression has a type, which indicates
/// the type of value it produces. The type is assigned when the expression is created and can be used to reference the
/// type from other nodes or from external code.
/// </summary>
public abstract class Expr(NodeID id, TextSpan span, TypID? type = null) : Node(id, span)
{
    /// <summary>
    /// The type of the expression. This is assigned when the expression is created and can be used to reference the type from other nodes or from external code.
    /// </summary>
    public TypID Type { get; set; } = type ?? TypID.Invalid;
}

/// <summary>
/// Represents a statement in the intermediate representation (IR) of the program. A statement is a node that performs
/// an action, and does not produce a value. Each statement can contain other statements or expressions as children, and
/// can be used to represent control flow, function calls, variable declarations, and other constructs in the program.
/// </summary>
public abstract class Stmt(NodeID id, TextSpan span) : Node(id, span);

/// <summary>
/// Represents a declaration in the intermediate representation (IR) of the program. A declaration is a node that introduces
/// a new entity in the program, such as a function, variable, type, or module. Each declaration can contain other
/// declarations, statements, or expressions as children, and can be used to represent the structure of the program and
/// the relationships between different entities. Declarations are typically used to define the interface and
/// implementation of functions, types, and modules, and to declare variables and other entities in the program.
/// </summary>
public abstract class Decl(NodeID id, TextSpan span) : Stmt(id, span)
{
    /// <summary>
    /// Parsed LuaCATS-style documentation comment attached to this declaration,
    /// populated by <see cref="Doc.DocBinder"/> after preparse.
    /// </summary>
    public Nebra.Doc.DocComment? Doc { get; set; }
}