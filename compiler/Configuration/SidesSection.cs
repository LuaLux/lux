namespace Nebra.Configuration;

/// <summary>
/// Path-glob → side-set resolver used by <c>Config.Sides</c> entries from
/// <c>nebra.toml</c>:
/// <code>
/// [sides]
/// "src/Client/**" = ["client", "shared"]
/// "src/Server/**" = ["server", "shared"]
/// "src/Shared/**" = ["shared"]
/// </code>
/// Files that don't match any glob inherit <see cref="Side.All"/> (permissive
/// default) so the feature is opt-in per project.
/// </summary>
public static class SidesResolver
{
    /// <summary>
    /// Resolves the side mask for a source file. Returns <see cref="Side.All"/>
    /// when the project doesn't configure <c>[sides]</c> at all, or when no
    /// glob matches — both cases preserve the pre-sides default so projects
    /// without side scoping keep their existing behaviour.
    /// </summary>
    /// <param name="scopes">Project's <c>[sides]</c> mapping (from
    /// <c>Config.Sides</c>).</param>
    /// <param name="filePath">Absolute or project-relative source path.</param>
    /// <param name="projectRoot">Project root used to compute relative paths
    /// for glob matching; if null, only the basename and full path are
    /// tested.</param>
    public static Side ResolveFileSide(IReadOnlyDictionary<string, List<string>> scopes, string filePath, string? projectRoot)
    {
        if (scopes == null || scopes.Count == 0) return Side.All;

        var rel = NormalizeRelative(filePath, projectRoot);
        var full = filePath.Replace('\\', '/');

        Side accumulated = Side.None;
        var matched = false;
        foreach (var (glob, names) in scopes)
        {
            if (!Match(glob, rel) && !Match(glob, full)) continue;
            matched = true;
            foreach (var n in names)
                accumulated |= SideExtensions.ParseSideName(n);
        }

        return matched ? (accumulated == Side.None ? Side.All : accumulated) : Side.All;
    }

    private static string NormalizeRelative(string filePath, string? projectRoot)
    {
        if (string.IsNullOrEmpty(projectRoot)) return filePath.Replace('\\', '/');
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(projectRoot);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var rel = full[root.Length..].TrimStart('/', '\\');
            return rel.Replace('\\', '/');
        }
        return full.Replace('\\', '/');
    }

    /// <summary>
    /// Minimal glob matcher: supports <c>*</c> (no slashes), <c>**</c> (any
    /// chars including slashes) and literal characters. Case-insensitive on
    /// all platforms so configs authored on Linux still match on Windows
    /// without surprises.
    /// </summary>
    internal static bool Match(string pattern, string input)
    {
        pattern = pattern.Replace('\\', '/');
        input = input.Replace('\\', '/');
        return MatchAt(pattern, 0, input, 0);
    }

    private static bool MatchAt(string p, int pi, string s, int si)
    {
        while (pi < p.Length)
        {
            var c = p[pi];
            if (c == '*')
            {
                var doubled = pi + 1 < p.Length && p[pi + 1] == '*';
                if (doubled)
                {
                    pi += 2;
                    if (pi < p.Length && p[pi] == '/') pi++;
                    if (pi >= p.Length) return true;
                    for (var k = si; k <= s.Length; k++)
                        if (MatchAt(p, pi, s, k)) return true;
                    return false;
                }
                else
                {
                    pi++;
                    if (pi >= p.Length) return !s.AsSpan(si).Contains('/');
                    for (var k = si; k <= s.Length; k++)
                    {
                        if (k > si && s[k - 1] == '/') return false;
                        if (MatchAt(p, pi, s, k)) return true;
                    }
                    return false;
                }
            }

            if (c == '?')
            {
                if (si >= s.Length || s[si] == '/') return false;
                pi++; si++;
                continue;
            }

            if (si >= s.Length) return false;
            if (char.ToLowerInvariant(c) != char.ToLowerInvariant(s[si])) return false;
            pi++; si++;
        }
        return si == s.Length;
    }
}
