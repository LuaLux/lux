using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn;

namespace Nebra.PackageManager;

/// <summary>
/// Resolved state of the dependency graph, persisted as <c>nebra.lock</c>. Entries are
/// sorted by name so the file is reproducible.
/// </summary>
public sealed class Lockfile
{
    public int Version { get; set; } = 1;

    public List<LockedPackage> Packages { get; set; } = [];

    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        IndentSize = 4,
        DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull
    };

    public static Lockfile? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return TomlSerializer.Deserialize<Lockfile>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Packages = Packages.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        foreach (var p in Packages)
            p.Deps = p.Deps.OrderBy(d => d, StringComparer.Ordinal).ToList();

        // Hand-rolled output. Tomlyn defaults to serializing List<T> as an
        // inline array of inline tables, which collapses every package onto
        // one line — terrible for git diffs. We want the standard
        // [[packages]] array-of-tables form so each field lives on its own
        // line and each package is its own block.
        var sb = new System.Text.StringBuilder();
        sb.Append("version = ").Append(Version).Append('\n');

        foreach (var p in Packages)
        {
            sb.Append('\n');
            sb.Append("[[packages]]\n");
            sb.Append("name = ").Append(TomlString(p.Name)).Append('\n');
            sb.Append("spec = ").Append(TomlString(p.Spec)).Append('\n');
            sb.Append("resolved = ").Append(TomlString(p.Resolved)).Append('\n');
            sb.Append("commit = ").Append(TomlString(p.Commit)).Append('\n');
            if (p.Version != null)
                sb.Append("version = ").Append(TomlString(p.Version)).Append('\n');
            sb.Append("integrity = ").Append(TomlString(p.Integrity)).Append('\n');
            if (p.Subdir != null)
                sb.Append("subdir = ").Append(TomlString(p.Subdir)).Append('\n');
            if (p.Deps.Count == 0)
            {
                sb.Append("deps = []\n");
            }
            else
            {
                sb.Append("deps = [\n");
                foreach (var d in p.Deps)
                    sb.Append("    ").Append(TomlString(d)).Append(",\n");
                sb.Append("]\n");
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string TomlString(string s)
    {
        // Basic TOML string: needs quotes around the value and any embedded "
        // or \ escaped. Lockfile values are paths / urls / hashes — no
        // multiline strings, no control chars, so this is sufficient.
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    public LockedPackage? Find(string name) =>
        Packages.FirstOrDefault(p => p.Name == name);
}

/// <summary>
/// One entry in the lockfile. All fields are populated at resolve time.
/// </summary>
public sealed class LockedPackage
{
    public string Name { get; set; } = "";

    /// <summary>Original spec as written in the manifest.</summary>
    public string Spec { get; set; } = "";

    /// <summary>Canonical source identifier, e.g. <c>github:foo/repo</c>.</summary>
    public string Resolved { get; set; } = "";

    /// <summary>Full commit SHA.</summary>
    public string Commit { get; set; } = "";

    /// <summary>Semver string if the ref was a semver tag.</summary>
    public string? Version { get; set; }

    /// <summary>Content hash of the package contents, <c>sha256-&lt;hex&gt;</c>.</summary>
    public string Integrity { get; set; } = "";

    /// <summary>Optional subdir within the repo that is the package root.</summary>
    public string? Subdir { get; set; }

    /// <summary>Dependency names — resolved packages are looked up by name in the same lockfile.</summary>
    public List<string> Deps { get; set; } = [];
}
