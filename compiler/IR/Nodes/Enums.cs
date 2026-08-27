namespace Nebra.IR;

public enum BinaryOp
{
    Add, Sub, Mul, Div, FloorDiv, Mod, Pow, Concat,
    Eq, Neq, Lt, Gt, Lte, Gte,
    And, Or, NilCoalesce,
    BitwiseAnd, BitwiseOr, BitwiseXor, LShift, RShift
}

public enum UnaryOp
{
    Negate, LogicalNot, Length, BitwiseNot
}

public enum ImportKind
{
    Named, Default, Namespace, SideEffect
}

public enum TableFieldKind
{
    Bracket, Named, Positional
}

public enum NumberKind
{
    Int, Hex, Float, HexFloat
}
