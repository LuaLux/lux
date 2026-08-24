using System.Text;

namespace Lux.Doc;

/// <summary>
/// Extracts and parses LuaCATS-style documentation comments from raw source text.
///
/// Two surface forms are recognised:
/// <list type="bullet">
///   <item>Stacks of contiguous <c>---</c> lines immediately above a declaration.</item>
///   <item>A single block comment of the form <c>--[[ ... ]]</c> sitting directly
///   above a declaration; lines inside may optionally be prefixed with <c>*</c>
///   or <c>--</c>.</item>
/// </list>
///
/// The parser intentionally accepts the LuaLS syntax verbatim — including tags
/// whose role partly overlaps with Lux's own grammar (<c>@param</c>'s type
/// annotation, etc.) — so existing Lua docs paste in unchanged. Tag types are
/// kept as raw text (<see cref="DocParam.TypeText"/>) rather than re-parsed.
/// </summary>
public static class DocCommentParser
{
    /// <summary>
    /// Returns the doc comment that immediately precedes the given line in the
    /// source, or null if no doc comment is found. <paramref name="targetLine"/>
    /// is 1-based, matching <see cref="Lux.Diagnostics.TextSpan.StartLn"/>.
    /// </summary>
    public static DocComment? ExtractAt(string source, int targetLine)
    {
        var lines = source.Split('\n');
        if (targetLine <= 1 || targetLine > lines.Length + 1) return null;

        var collected = TryCollectLineDoc(lines, targetLine - 2);
        if (collected != null) return Parse(collected);

        var block = TryCollectBlockDoc(lines, targetLine - 2);
        if (block != null) return Parse(block);

        return null;
    }

    /// <summary>
    /// Parses an already-extracted list of doc lines (with the leading
    /// <c>---</c> / <c>*</c> markers already stripped). Useful from contexts
    /// that have collected the comment text by other means.
    /// </summary>
    public static DocComment Parse(IReadOnlyList<string> lines) => ParseInternal(lines);

    private static List<string>? TryCollectLineDoc(string[] lines, int startIndex)
    {
        if (startIndex < 0) return null;
        var collected = new List<string>();
        for (var i = startIndex; i >= 0; i--)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("---"))
            {
                var content = trimmed[3..];
                if (content.Length > 0 && content[0] == ' ') content = content[1..];
                collected.Insert(0, content);
                continue;
            }
            break;
        }
        return collected.Count > 0 ? collected : null;
    }

    private static List<string>? TryCollectBlockDoc(string[] lines, int startIndex)
    {
        if (startIndex < 0) return null;

        var endIdx = startIndex;
        while (endIdx >= 0 && string.IsNullOrWhiteSpace(lines[endIdx])) endIdx--;
        if (endIdx < 0) return null;

        var endLine = lines[endIdx].TrimEnd('\r');
        var closeIdx = endLine.LastIndexOf("]]", StringComparison.Ordinal);
        if (closeIdx < 0) return null;

        var startBlockIdx = -1;
        for (var i = endIdx; i >= 0; i--)
        {
            var l = lines[i].TrimEnd('\r');
            var open = l.IndexOf("--[[", StringComparison.Ordinal);
            if (open >= 0) { startBlockIdx = i; break; }
        }
        if (startBlockIdx < 0) return null;

        var collected = new List<string>();
        for (var i = startBlockIdx; i <= endIdx; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (i == startBlockIdx)
            {
                var open = l.IndexOf("--[[", StringComparison.Ordinal);
                l = l[(open + 4)..];
            }
            if (i == endIdx)
            {
                var close = l.LastIndexOf("]]", StringComparison.Ordinal);
                if (close >= 0) l = l[..close];
            }

            var trimmed = l.TrimStart();
            if (trimmed.StartsWith("--")) trimmed = trimmed[2..];
            if (trimmed.StartsWith("*")) trimmed = trimmed[1..];
            if (trimmed.StartsWith(" ")) trimmed = trimmed[1..];
            collected.Add(trimmed);
        }

        while (collected.Count > 0 && string.IsNullOrWhiteSpace(collected[0])) collected.RemoveAt(0);
        while (collected.Count > 0 && string.IsNullOrWhiteSpace(collected[^1])) collected.RemoveAt(collected.Count - 1);

        return collected.Count > 0 ? collected : null;
    }

    private static DocComment ParseInternal(IReadOnlyList<string> lines)
    {
        var summary = new StringBuilder();
        var remarks = new StringBuilder();
        var current = summary;
        var summaryDone = false;

        var parameters = new List<DocParam>();
        var returns = new List<DocReturn>();
        var see = new List<string>();
        var examples = new List<string>();
        var overloads = new List<DocOverload>();
        var generics = new List<DocGeneric>();
        var customTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var deprecated = false;
        string? deprecatedReason = null;
        var async = false;
        var nodiscard = false;
        string? since = null;
        var visibility = DocVisibility.Default;

        var pendingExample = new StringBuilder();
        var inExample = false;

        void FlushExample()
        {
            if (!inExample) return;
            var text = pendingExample.ToString().TrimEnd();
            if (text.Length > 0) examples.Add(text);
            pendingExample.Clear();
            inExample = false;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith('@'))
            {
                if (inExample)
                {
                    pendingExample.AppendLine(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!summaryDone && summary.Length > 0)
                    {
                        summaryDone = true;
                        current = remarks;
                    }
                    else if (current.Length > 0)
                    {
                        current.AppendLine();
                    }
                    continue;
                }

                if (current.Length > 0) current.Append(' ');
                current.Append(line.Trim());
                continue;
            }

            FlushExample();

            var spaceIdx = trimmed.IndexOf(' ');
            var tagName = spaceIdx < 0 ? trimmed[1..] : trimmed[1..spaceIdx];
            var rest = spaceIdx < 0 ? string.Empty : trimmed[(spaceIdx + 1)..].Trim();

            switch (tagName.ToLowerInvariant())
            {
                case "param":
                    parameters.Add(ParseParam(rest));
                    break;
                case "return":
                case "returns":
                    returns.Add(ParseReturn(rest));
                    break;
                case "deprecated":
                    deprecated = true;
                    if (rest.Length > 0) deprecatedReason = rest;
                    break;
                case "async":
                    async = true;
                    break;
                case "nodiscard":
                    nodiscard = true;
                    break;
                case "since":
                    since = rest;
                    break;
                case "see":
                    if (rest.Length > 0) see.Add(rest);
                    break;
                case "example":
                    inExample = true;
                    if (rest.Length > 0) pendingExample.AppendLine(rest);
                    break;
                case "overload":
                    if (rest.Length > 0) overloads.Add(new DocOverload(rest));
                    break;
                case "generic":
                    foreach (var g in ParseGenerics(rest)) generics.Add(g);
                    break;
                case "private":
                    visibility = DocVisibility.Private;
                    break;
                case "protected":
                    visibility = DocVisibility.Protected;
                    break;
                case "public":
                    visibility = DocVisibility.Public;
                    break;
                case "package":
                    visibility = DocVisibility.Package;
                    break;
                default:
                    if (!customTags.TryGetValue(tagName, out var list))
                    {
                        list = [];
                        customTags[tagName] = list;
                    }
                    list.Add(rest);
                    break;
            }
        }

        FlushExample();

        return new DocComment
        {
            Summary = summary.ToString().Trim(),
            Remarks = remarks.ToString().Trim(),
            Params = parameters,
            Returns = returns,
            See = see,
            Examples = examples,
            Overloads = overloads,
            Generics = generics,
            Deprecated = deprecated,
            DeprecatedReason = deprecatedReason,
            Async = async,
            NoDiscard = nodiscard,
            Since = since,
            Visibility = visibility,
            Custom = customTags
        };
    }

    private static DocParam ParseParam(string rest)
    {
        var (name, after) = TakeWord(rest);
        var optional = false;
        if (name.EndsWith('?')) { name = name[..^1]; optional = true; }

        string? typeText = null;
        var description = after;
        if (LooksLikeTypeStart(after))
        {
            var (t, descAfter) = TakeBalanced(after);
            if (LooksLikeType(t))
            {
                typeText = t;
                description = descAfter;
            }
        }
        description = description.TrimStart('-', ' ', '#').TrimEnd();
        return new DocParam(name, optional, typeText, description);
    }

    private static DocReturn ParseReturn(string rest)
    {
        string? typeText = null;
        var after = rest;
        if (LooksLikeTypeStart(rest))
        {
            var (t, descAfter) = TakeBalanced(rest);
            if (LooksLikeType(t))
            {
                typeText = t;
                after = descAfter;
            }
        }
        var description = after.Trim();
        string? name = null;

        // LuaCATS allows "<type> <name> #<comment>" — only honour it when the
        // explicit '#' separator is present, otherwise the rest is description.
        var hashIdx = description.IndexOf(" #", StringComparison.Ordinal);
        if (hashIdx > 0)
        {
            var nameCandidate = description[..hashIdx].Trim();
            if (IsSimpleIdent(nameCandidate))
            {
                name = nameCandidate;
                description = description[(hashIdx + 2)..].TrimStart();
            }
        }
        else if (description.StartsWith('#'))
        {
            description = description[1..].TrimStart();
        }

        return new DocReturn(name, typeText, description);
    }

    private static bool LooksLikeTypeStart(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var c = s[0];
        return c == '(' || c == '[' || c == '{' || c == '<' || char.IsLetter(c) || c == '_';
    }

    /// <summary>
    /// Heuristic for distinguishing a real type expression from a stray
    /// English word (the user wrote <c>@param name the actual name</c>).
    /// Accepts anything containing type punctuation, the LuaCATS primitives,
    /// or a leading uppercase identifier (class/interface convention).
    /// </summary>
    private static bool LooksLikeType(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.IndexOfAny(['|', '?', '[', ']', '<', '>', '(', ')', '{', '}', ':', '.', ',']) >= 0)
            return true;
        var primitives = new[] { "string", "number", "boolean", "integer", "any", "nil", "void", "never", "table", "thread", "userdata", "function", "self" };
        foreach (var prim in primitives)
            if (string.Equals(s, prim, StringComparison.Ordinal)) return true;
        if (char.IsUpper(s[0])) return true;
        return false;
    }

    private static bool IsSimpleIdent(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return true;
    }

    private static IEnumerable<DocGeneric> ParseGenerics(string rest)
    {
        if (string.IsNullOrWhiteSpace(rest)) yield break;
        foreach (var raw in rest.Split(','))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var colon = entry.IndexOf(':');
            if (colon < 0) yield return new DocGeneric(entry, null);
            else yield return new DocGeneric(entry[..colon].Trim(), entry[(colon + 1)..].Trim());
        }
    }

    private static (string word, string rest) TakeWord(string input)
    {
        var i = 0;
        while (i < input.Length && !char.IsWhiteSpace(input[i])) i++;
        var word = input[..i];
        var rest = i < input.Length ? input[(i + 1)..].TrimStart() : string.Empty;
        return (word, rest);
    }

    /// <summary>
    /// Takes a balanced type expression off the front of <paramref name="input"/>.
    /// Handles nesting in <c>(...)</c>, <c>[...]</c>, <c>&lt;...&gt;</c>,
    /// <c>{...}</c> so things like <c>fun(x: number): string</c> or
    /// <c>table&lt;string, number&gt;</c> stay together. Returns an empty
    /// string when the input doesn't start with a recognisable type.
    /// </summary>
    private static (string typeText, string rest) TakeBalanced(string input)
    {
        if (string.IsNullOrEmpty(input)) return (string.Empty, string.Empty);
        var depth = 0;
        var i = 0;
        var sawAny = false;
        while (i < input.Length)
        {
            var c = input[i];
            if (depth == 0 && char.IsWhiteSpace(c)) break;
            if (depth == 0 && (c == '#' || c == ',') && sawAny) break;
            if (c == '(' || c == '[' || c == '<' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '>' || c == '}') depth--;
            sawAny = true;
            i++;
        }
        var type = input[..i];
        var rest = i < input.Length ? input[i..].TrimStart() : string.Empty;
        return (type, rest);
    }
}
