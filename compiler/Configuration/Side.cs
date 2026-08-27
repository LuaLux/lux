namespace Nebra.Configuration;

/// <summary>
/// Execution-context flags for symbols and source files (Garry's-Mod / FiveM /
/// nanos-style multiplayer split). A symbol annotated with <c>@side(...)</c>
/// carries one or more of these bits — typically just one of
/// <see cref="Client"/>, <see cref="Server"/>, or <see cref="Shared"/>. A file
/// in <c>nebra.toml</c>'s <c>[sides]</c> mapping carries its accepted bits — a
/// server file usually accepts <c>server | shared</c>.
/// </summary>
/// <remarks>
/// <c>shared</c> is its own bit (NOT the union of client+server) because
/// "shared code" is a separate notion from "code that runs on both client and
/// server" — shared code is typically pure data / types that you want to
/// reach from both sides without forking. Unannotated symbols default to
/// <see cref="All"/> (wildcard) so they remain reachable from any file,
/// preserving the pre-sides default.
/// </remarks>
[Flags]
public enum Side
{
    None = 0,
    Client = 1 << 0,
    Server = 1 << 1,
    Shared = 1 << 2,
    All = Client | Server | Shared,
}

public static class SideExtensions
{
    /// <summary>
    /// Parses a side name as written in <c>@side(...)</c> or <c>nebra.toml</c>.
    /// Recognised: <c>client</c>, <c>server</c>, <c>shared</c>, <c>all</c>.
    /// Case-insensitive. Returns <see cref="Side.None"/> for unknown names so
    /// callers can decide whether to report a diagnostic or fall back.
    /// </summary>
    public static Side ParseSideName(string name) => name?.ToLowerInvariant() switch
    {
        "client" => Side.Client,
        "server" => Side.Server,
        "shared" => Side.Shared,
        "all" => Side.All,
        _ => Side.None,
    };

    /// <summary>
    /// Renders a side mask back to a stable, human-friendly label suitable
    /// for diagnostics. Single-bit masks render as the side name; multi-bit
    /// masks render as <c>"a+b"</c>; <see cref="All"/> renders as <c>"any"</c>.
    /// </summary>
    public static string Format(this Side side)
    {
        if (side == Side.None) return "none";
        if (side == Side.All) return "any";
        var parts = new List<string>();
        if ((side & Side.Client) != 0) parts.Add("client");
        if ((side & Side.Server) != 0) parts.Add("server");
        if ((side & Side.Shared) != 0) parts.Add("shared");
        return string.Join("+", parts);
    }

    /// <summary>
    /// Returns true when a symbol carrying <paramref name="symbolSide"/> is
    /// reachable from a file whose accepted mask is <paramref name="fileMask"/>.
    /// Unannotated symbols carry <see cref="Side.All"/> (wildcard) and are
    /// always accessible. For annotated symbols, every bit in the symbol's
    /// mask must be present in the file's accepted mask — a server-only
    /// symbol is only reachable from files that accept <c>server</c>.
    /// </summary>
    public static bool IsAccessibleFrom(this Side symbolSide, Side fileMask)
    {
        if (fileMask == Side.None) return true;
        if (symbolSide == Side.All) return true;
        return (symbolSide & fileMask) == symbolSide;
    }
}
