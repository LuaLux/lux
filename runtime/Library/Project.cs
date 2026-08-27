using System.Diagnostics;
using System.Text;
using Nebra.Runtime.Bindings;

namespace Nebra.Runtime.Library;

/// <summary>
/// High-level helpers for writing project scaffolding from setup scripts: <c>.gitignore</c>,
/// <c>nebra.toml</c>, and shell invocations. Exposed to Lua as the <c>project</c> global.
/// </summary>
[NebraExport("project")]
public sealed class Project
{
    private static readonly string[] DefaultGitignore =
    [
        "nebra_modules/",
        "out/",
        "*.log",
        ".DS_Store",
        ".vscode/",
        ".idea/",
    ];

    /// <summary>
    /// Creates or updates a <c>.gitignore</c> at <paramref name="path"/> with Nebra defaults
    /// plus any extra lines supplied by the caller. Existing entries are preserved and
    /// duplicates are skipped.
    /// </summary>
    [NebraExport("writeGitignore")]
    public static void WriteGitignore(string path, IList<object?>? extra = null)
    {
        var lines = new List<string>(DefaultGitignore);
        if (extra != null)
            foreach (var e in extra)
                if (e is string s && !string.IsNullOrWhiteSpace(s)) lines.Add(s.Trim());

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Join('\n', lines.Distinct()) + "\n");
            return;
        }

        var content = File.ReadAllText(path);
        var existing = new HashSet<string>(content.Split('\n').Select(l => l.Trim()));
        var toAppend = lines.Where(l => !existing.Contains(l.Trim())).Distinct().ToList();
        if (toAppend.Count == 0) return;
        if (!content.EndsWith('\n')) content += "\n";
        content += string.Join('\n', toAppend) + "\n";
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Writes a <c>nebra.toml</c> file from a plain table. Supports scalar keys, nested
    /// tables, and <c>dependencies</c> / <c>dev_dependencies</c> / <c>peer_dependencies</c>
    /// which are emitted as dedicated <c>[section]</c> blocks.
    /// </summary>
    [NebraExport("writeConfig")]
    public static void WriteConfig(string path, IDictionary<string, object?> settings)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, EmitToml(settings));
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/>, creating any
    /// missing parent directories. Used by setup scripts to lay down arbitrary
    /// text files (Index.neb entries, sidecar manifests, etc.) without dropping
    /// to <c>io.open</c>.
    /// </summary>
    [NebraExport("writeFile")]
    public static void WriteFile(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, contents);
    }

    /// <summary>
    /// Returns the absolute path of the process's current working directory.
    /// Setup scripts should prefer this over <c>os.getenv("PWD")</c>, which
    /// reads the shell's PWD variable — that does NOT follow C#'s
    /// <c>Environment.CurrentDirectory</c> change made by the create flow,
    /// so it would point at the directory `nebra create` was launched from
    /// instead of the freshly-created target dir.
    /// </summary>
    [NebraExport("cwd")]
    public static string Cwd()
    {
        return Environment.CurrentDirectory;
    }

    /// <summary>
    /// Runs a shell command in <paramref name="cwd"/> (or the current working directory
    /// if nil). Returns the exit code. Output is inherited so users see progress.
    /// </summary>
    [NebraExport("runShell")]
    public static long RunShell(string command, string? cwd = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = string.IsNullOrEmpty(cwd) ? Environment.CurrentDirectory : cwd,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException($"failed to start shell command: {command}");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// Runs <c>git init</c> (and optionally <c>git add .</c> + initial commit) in the
    /// given directory. Returns true when every step exits with 0.
    /// </summary>
    [NebraExport("gitInit")]
    public static bool GitInit(string directory, bool initialCommit = false, string? commitMessage = null)
    {
        if (RunShell("git init", directory) != 0) return false;
        if (!initialCommit) return true;
        if (RunShell("git add .", directory) != 0) return false;
        var msg = commitMessage ?? "Initial commit";
        return RunShell($"git commit -m \"{msg.Replace("\"", "\\\"")}\"", directory) == 0;
    }

    /// <summary>
    /// Convenience wrapper that runs <c>nebra install</c> in <paramref name="directory"/>.
    /// </summary>
    [NebraExport("installDeps")]
    public static bool InstallDeps(string? directory = null, bool allowScripts = false)
    {
        var cmd = allowScripts ? "nebra install --allow-scripts" : "nebra install";
        return RunShell(cmd, directory) == 0;
    }

    private static string EmitToml(IDictionary<string, object?> settings)
    {
        var sb = new StringBuilder();
        var sections = new List<(string Name, IDictionary<string, object?> Values)>();

        foreach (var (key, value) in settings)
        {
            if (value is IDictionary<string, object?> subDict)
            {
                sections.Add((key, subDict));
                continue;
            }
            EmitKeyValue(sb, key, value);
        }

        foreach (var (name, values) in sections)
        {
            sb.Append('\n');
            sb.Append('[').Append(name).Append(']').Append('\n');
            foreach (var (key, value) in values)
                EmitKeyValue(sb, key, value);
        }

        return sb.ToString();
    }

    private static void EmitKeyValue(StringBuilder sb, string key, object? value)
    {
        sb.Append(EscapeKey(key)).Append(" = ").Append(EmitValue(value)).Append('\n');
    }

    private static string EmitValue(object? value)
    {
        switch (value)
        {
            case null: return "\"\"";
            case bool b: return b ? "true" : "false";
            case long l: return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case int i: return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case double d: return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case float f: return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case string s: return EscapeString(s);
            case IDictionary<string, object?> dict:
            {
                var parts = dict.Select(kv => $"{EscapeKey(kv.Key)} = {EmitValue(kv.Value)}");
                return "{ " + string.Join(", ", parts) + " }";
            }
            case System.Collections.IList list:
            {
                var items = new List<string>();
                foreach (var item in list) items.Add(EmitValue(item));
                return "[" + string.Join(", ", items) + "]";
            }
            default: return EscapeString(value.ToString() ?? "");
        }
    }

    private static string EscapeKey(string key)
    {
        var isBare = key.Length > 0 && key.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
        return isBare ? key : EscapeString(key);
    }

    private static string EscapeString(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        return "\"" + escaped + "\"";
    }
}
