using System.Text.RegularExpressions;

namespace Nebra.PackageManager;

public enum SpecKind
{
    /// <summary>Bare name (optionally with range), resolved via the alias index.</summary>
    Alias,
    /// <summary>Git repository, either host-shortcut or full URL.</summary>
    Git,
    /// <summary>Local filesystem path.</summary>
    File
}

/// <summary>
/// Dependency source descriptor. Produced by <see cref="Parse"/> (string form) or
/// <see cref="FromValue"/> (TOML scalar/table form).
/// </summary>
public sealed class PackageSpec
{
    public SpecKind Kind { get; init; }
    public string Raw { get; init; } = "";

    // Alias
    public string? AliasName { get; init; }
    public string? AliasRange { get; init; }

    // Git
    public string? Host { get; init; }
    public string? Owner { get; init; }
    public string? Repo { get; init; }
    public string? CloneUrl { get; init; }
    public string? Ref { get; init; }
    public string? Subdir { get; init; }

    // File
    public string? Path { get; init; }

    /// <summary>
    /// Stable identifier usable as a store-path root. Host+owner+repo for git,
    /// &quot;file&quot;+absolute path for file, &quot;alias&quot;+name for unresolved aliases.
    /// </summary>
    public string Identity()
    {
        return Kind switch
        {
            SpecKind.Git => $"{Host}/{Owner}/{Repo}",
            SpecKind.File => "file:" + Path,
            SpecKind.Alias => "alias:" + AliasName,
            _ => Raw
        };
    }

    public override string ToString() => Raw;

    /// <summary>
    /// Parses a string specifier. Supported forms:
    ///   github:owner/repo[@ref]    gitlab:owner/repo[@ref]    bitbucket:owner/repo[@ref]
    ///   sr.ht:~user/repo[@ref]     git+https://host/repo.git[#ref]   git+ssh://...
    ///   https://github.com/owner/repo(.git)?[#ref]
    ///   file:../path
    ///   name[@range]               (bare specifier; resolved via alias index later)
    /// </summary>
    public static PackageSpec Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new PackageManagerException("empty package specifier");
        var s = input.Trim();

        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new PackageSpec { Kind = SpecKind.File, Raw = input, Path = s[5..] };

        if (s.StartsWith("git+", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUrl(input, s);
        }

        var colon = s.IndexOf(':');
        if (colon > 0)
        {
            var scheme = s[..colon].ToLowerInvariant();
            if (IsKnownHostShortcut(scheme))
                return ParseHostShortcut(input, scheme, s[(colon + 1)..]);
        }

        return ParseAlias(input, s);
    }

    /// <summary>
    /// Parses a TOML value (string or dictionary) into a spec. Supports both the
    /// string form and the <c>{ git = &quot;...&quot;, tag = &quot;v1&quot;, subdir = &quot;...&quot; }</c> object form.
    /// </summary>
    public static PackageSpec FromValue(object? value)
    {
        if (value is string str) return Parse(str);

        if (value is IDictionary<string, object?> dict)
            return FromDict(dict);

        if (value is System.Collections.IDictionary idict)
        {
            var converted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in idict)
                converted[entry.Key.ToString() ?? ""] = entry.Value;
            return FromDict(converted);
        }

        throw new PackageManagerException($"unsupported dependency value type '{value?.GetType().Name ?? "null"}'");
    }

    private static PackageSpec FromDict(IDictionary<string, object?> dict)
    {
        string? Get(params string[] keys)
        {
            foreach (var k in keys)
                if (dict.TryGetValue(k, out var v) && v is not null) return v.ToString();
            return null;
        }

        var path = Get("path", "file");
        if (path != null)
            return new PackageSpec { Kind = SpecKind.File, Raw = "file:" + path, Path = path };

        var git = Get("git", "url", "repository");
        if (git != null)
        {
            var @ref = Get("tag", "branch", "commit", "rev", "ref");
            var subdir = Get("subdir", "directory", "path_in_repo");
            var parsed = ParseUrl(git + (@ref != null ? "#" + @ref : ""), (git + (@ref != null ? "#" + @ref : "")));
            return new PackageSpec
            {
                Kind = SpecKind.Git,
                Raw = parsed.Raw,
                Host = parsed.Host,
                Owner = parsed.Owner,
                Repo = parsed.Repo,
                CloneUrl = parsed.CloneUrl,
                Ref = @ref ?? parsed.Ref,
                Subdir = subdir,
            };
        }

        var alias = Get("name", "alias");
        if (alias != null)
        {
            var range = Get("version", "range");
            return new PackageSpec
            {
                Kind = SpecKind.Alias,
                Raw = alias + (range != null ? "@" + range : ""),
                AliasName = alias,
                AliasRange = range,
            };
        }

        throw new PackageManagerException("dependency table must contain 'git', 'path', or 'name'");
    }

    private static bool IsKnownHostShortcut(string scheme) =>
        scheme is "github" or "gitlab" or "bitbucket" or "sr.ht" or "codeberg";

    private static PackageSpec ParseHostShortcut(string raw, string scheme, string rest)
    {
        string? @ref = null;
        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            @ref = rest[(at + 1)..];
            rest = rest[..at];
        }

        var parts = rest.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new PackageManagerException($"invalid spec '{raw}': expected 'owner/repo'");

        var owner = parts[0];
        var repo = parts[1];
        string? subdir = parts.Length > 2 ? parts[2] : null;

        var host = scheme switch
        {
            "github" => "github.com",
            "gitlab" => "gitlab.com",
            "bitbucket" => "bitbucket.org",
            "sr.ht" => "git.sr.ht",
            "codeberg" => "codeberg.org",
            _ => scheme
        };

        var cloneUrl = scheme == "sr.ht"
            ? $"https://git.sr.ht/{owner}/{repo}"
            : $"https://{host}/{owner}/{repo}.git";

        return new PackageSpec
        {
            Kind = SpecKind.Git,
            Raw = raw,
            Host = scheme,
            Owner = owner,
            Repo = repo,
            CloneUrl = cloneUrl,
            Ref = @ref,
            Subdir = subdir,
        };
    }

    private static readonly Regex UrlRegex = new(
        @"^(?:git\+)?(?<proto>https?|ssh|git)://(?:(?<userinfo>[^@/]+)@)?(?<host>[^:/]+)(?::(?<port>\d+))?/(?<path>[^#]+?)(?:\.git)?(?:#(?<ref>.+))?$",
        RegexOptions.Compiled);

    private static PackageSpec ParseUrl(string raw, string s)
    {
        string? @ref = null;
        var hashIdx = s.IndexOf('#');
        if (hashIdx >= 0)
        {
            @ref = s[(hashIdx + 1)..];
        }

        var m = UrlRegex.Match(s);
        if (!m.Success)
            throw new PackageManagerException($"invalid git url '{raw}'");

        var host = m.Groups["host"].Value;
        var pathPart = m.Groups["path"].Value;
        var segs = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2)
            throw new PackageManagerException($"invalid git url '{raw}': expected '/owner/repo'");
        var owner = segs[0];
        var repo = segs[1];

        // Build a clean clone URL without the #ref fragment and the optional git+ prefix.
        var cleaned = s.StartsWith("git+", StringComparison.OrdinalIgnoreCase) ? s[4..] : s;
        if (hashIdx >= 0)
        {
            // re-slice relative to the un-prefixed string
            var idx = cleaned.IndexOf('#');
            if (idx >= 0) cleaned = cleaned[..idx];
        }
        if (!cleaned.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            cleaned += ".git";

        return new PackageSpec
        {
            Kind = SpecKind.Git,
            Raw = raw,
            Host = host,
            Owner = owner,
            Repo = repo,
            CloneUrl = cleaned,
            Ref = @ref,
        };
    }

    private static PackageSpec ParseAlias(string raw, string s)
    {
        string name;
        string? range = null;
        var at = s.LastIndexOf('@');
        if (at > 0) // avoid @scope at position 0
        {
            name = s[..at];
            range = s[(at + 1)..];
        }
        else
        {
            name = s;
        }

        return new PackageSpec
        {
            Kind = SpecKind.Alias,
            Raw = raw,
            AliasName = name,
            AliasRange = range,
        };
    }
}
