namespace Nebra;

/// <summary>
/// Migrates a project written for the old <c>lux</c> toolchain to Nebra.
/// <para>
/// The rename touches file names (<c>*.lux</c>, <c>lux.toml</c>, <c>lux.lock</c>) and a fixed set
/// of tokens inside text files. Replacement is deliberately token-based rather than a blanket
/// <c>lux</c> to <c>nebra</c> substitution, so a user's own identifier called <c>lux</c> or a
/// sentence mentioning lux meters survives untouched.
/// </para>
/// </summary>
public static class Migrator
{
    /// <summary>Directory the pre-migration copy is written to.</summary>
    private const string BackupDirName = ".nebra-migrate-backup";

    /// <summary>Directories never descended into, whatever the project layout is.</summary>
    private static readonly string[] SkippedDirs =
    [
        ".git", ".svn", ".hg", "node_modules", "lux_modules", "nebra_modules",
        "bin", "obj", BackupDirName
    ];

    /// <summary>File types whose contents are rewritten. Everything else is only ever renamed.</summary>
    private static readonly string[] TextExtensions =
    [
        ".lux", ".neb", ".lua", ".toml", ".md", ".json", ".yml", ".yaml",
        ".sh", ".fish", ".ps1", ".txt", ".gitignore"
    ];

    /// <summary>
    /// Token replacements, applied longest-first so that the specific forms are consumed before
    /// the generic ones can split them apart.
    /// </summary>
    private static readonly (string Old, string New)[] TokenRules =
    [
        ("lux_modules", "nebra_modules"),
        ("lux.toml", "nebra.toml"),
        ("lux.lock", "nebra.lock"),
        ("lux:test", "nebra:test"),
        ("__lux_reflect", "__nebra_reflect"),
        ("__lux_", "__nebra_"),
        ("__lux", "__nebra"),
        ("LUX_", "NEBRA_"),
        (".d.lux", ".d.neb"),
        (".lux", ".neb"),
    ];

    /// <summary>
    /// Subcommands that make a bare <c>lux</c> unambiguously a CLI invocation. Used to rewrite
    /// build scripts and READMEs without touching prose that merely contains the word.
    /// </summary>
    private static readonly string[] Subcommands =
    [
        "build", "watch", "run", "init", "create", "install", "add", "remove", "rm",
        "registry", "pm", "docs", "test", "compile", "repl", "lps", "check",
        "upgrade", "version", "help", "migrate"
    ];

    private sealed record Rename(string From, string To);

    private sealed record Edit(string Path, int Replacements);

    /// <summary>
    /// Entry point for <c>nebra migrate</c>. Accepts <c>--dry-run</c> to report the plan without
    /// writing, <c>--no-backup</c> to skip the safety copy, and an optional project directory
    /// (defaults to the current one).
    /// </summary>
    public static int Run(string[] args)
    {
        var dryRun = false;
        var noBackup = false;
        string? target = null;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--dry-run" or "-n":
                    dryRun = true;
                    break;
                case "--no-backup":
                    noBackup = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Unknown flag '{arg}'. Run 'nebra migrate --help'.");
                        return 1;
                    }
                    target = arg;
                    break;
            }
        }

        var root = Path.GetFullPath(target ?? Environment.CurrentDirectory);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Directory not found: {root}");
            return 1;
        }

        var legacyConfig = Path.Combine(root, "lux.toml");
        var alreadyMigrated = File.Exists(Path.Combine(root, "nebra.toml"));
        if (!File.Exists(legacyConfig) && !alreadyMigrated && !HasLegacySources(root))
        {
            Console.Error.WriteLine($"No lux.toml and no *.lux files under {root}.");
            Console.Error.WriteLine("Nothing to migrate. Point migrate at the project root.");
            return 1;
        }

        Console.WriteLine($"Scanning {root}");
        Console.WriteLine();

        var outputDir = ReadOutputDir(root);
        var files = Collect(root, outputDir);

        var renames = new List<Rename>();
        var edits = new List<Edit>();

        foreach (var path in files)
        {
            var renamed = TargetName(path);
            if (renamed != path) renames.Add(new Rename(path, renamed));

            if (!IsTextFile(path)) continue;
            var replacements = CountReplacements(path);
            if (replacements > 0) edits.Add(new Edit(renamed, replacements));
        }

        foreach (var r in renames)
            Console.WriteLine($"  rename  {Rel(root, r.From),-44} -> {Rel(root, r.To)}");
        foreach (var e in edits)
            Console.WriteLine($"  edit    {Rel(root, e.Path),-44}    {e.Replacements} replacement(s)");

        var legacyModules = Path.Combine(root, "lux_modules");
        var hasLegacyModules = Directory.Exists(legacyModules);

        if (renames.Count == 0 && edits.Count == 0 && !hasLegacyModules)
        {
            Console.WriteLine("  nothing to do, this project already looks like Nebra");
            return 0;
        }

        var totalReplacements = edits.Sum(e => e.Replacements);
        Console.WriteLine();

        if (dryRun)
        {
            Console.WriteLine($"{renames.Count} rename(s), {edits.Count} file(s) edited, {totalReplacements} replacement(s).");
            if (hasLegacyModules) PrintModulesNote();
            Console.WriteLine("Nothing written (--dry-run).");
            return 0;
        }

        if (!noBackup)
        {
            var backup = Path.Combine(root, BackupDirName);
            if (Directory.Exists(backup))
            {
                Console.Error.WriteLine($"{BackupDirName}/ already exists. Remove it or pass --no-backup.");
                return 1;
            }

            CopyTree(root, backup, outputDir);
            Console.WriteLine($"Backup written to {BackupDirName}/");
        }

        foreach (var r in renames)
        {
            if (File.Exists(r.To))
            {
                Console.Error.WriteLine($"Refusing to overwrite existing {Rel(root, r.To)}.");
                return 1;
            }

            File.Move(r.From, r.To);
        }

        foreach (var e in edits)
            ApplyReplacements(e.Path);

        Console.WriteLine($"{renames.Count} rename(s), {edits.Count} file(s) edited, {totalReplacements} replacement(s).");
        if (hasLegacyModules) PrintModulesNote();
        Console.WriteLine();
        Console.WriteLine("Migration complete. Run 'nebra build' to verify.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: nebra migrate [dir] [flags]");
        Console.WriteLine();
        Console.WriteLine("Converts a project built for the old 'lux' toolchain to Nebra:");
        Console.WriteLine("renames *.lux to *.neb, lux.toml to nebra.toml, lux.lock to nebra.lock,");
        Console.WriteLine("and rewrites the matching tokens inside text files.");
        Console.WriteLine();
        Console.WriteLine("Flags:");
        Console.WriteLine("  -n, --dry-run    Report what would change without writing anything");
        Console.WriteLine("      --no-backup  Skip the .nebra-migrate-backup/ copy");
    }

    private static void PrintModulesNote()
    {
        Console.WriteLine();
        Console.WriteLine("  note  lux_modules/ was left untouched because its contents are fetched,");
        Console.WriteLine("        not authored. Delete it and run 'nebra install' to refetch.");
    }

    private static bool HasLegacySources(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lux", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <c>output</c> out of an existing lux.toml or nebra.toml so the generated Lua is not
    /// migrated. Falls back to <c>out</c>, which is the default for both.
    /// </summary>
    private static string ReadOutputDir(string root)
    {
        foreach (var name in new[] { "lux.toml", "nebra.toml" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("output", StringComparison.Ordinal)) continue;
                var eq = trimmed.IndexOf('=');
                if (eq < 0) continue;
                var value = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
                if (value.Length > 0) return value;
            }
        }

        return "out";
    }

    private static List<string> Collect(string root, string outputDir)
    {
        var result = new List<string>();
        var skipOutput = Path.GetFullPath(Path.Combine(root, outputDir));
        Walk(root);
        result.Sort(StringComparer.Ordinal);
        return result;

        void Walk(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                result.Add(file);

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkippedDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (Path.GetFullPath(sub) == skipOutput) continue;
                Walk(sub);
            }
        }
    }

    private static string TargetName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);

        var renamed = name switch
        {
            "lux.toml" => "nebra.toml",
            "lux.lock" => "nebra.lock",
            _ => name.EndsWith(".lux", StringComparison.OrdinalIgnoreCase)
                ? name[..^4] + ".neb"
                : name
        };

        return renamed == name ? path : Path.Combine(dir, renamed);
    }

    private static bool IsTextFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(path);
        return TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0) return null;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static int CountReplacements(string path)
    {
        var text = ReadText(path);
        if (text == null) return 0;
        Rewrite(text, out var count);
        return count;
    }

    private static void ApplyReplacements(string path)
    {
        var text = ReadText(path);
        if (text == null) return;
        var rewritten = Rewrite(text, out var count);
        if (count > 0) File.WriteAllText(path, rewritten);
    }

    private static string Rewrite(string text, out int count)
    {
        count = 0;

        foreach (var (oldToken, newToken) in TokenRules)
        {
            var occurrences = Occurrences(text, oldToken);
            if (occurrences == 0) continue;
            count += occurrences;
            text = text.Replace(oldToken, newToken, StringComparison.Ordinal);
        }

        foreach (var sub in Subcommands)
        {
            foreach (var prefix in new[] { "lux ", "`lux ", "$ lux " })
            {
                var needle = prefix + sub;
                var occurrences = Occurrences(text, needle);
                if (occurrences == 0) continue;
                count += occurrences;
                text = text.Replace(needle, prefix.Replace("lux", "nebra") + sub, StringComparison.Ordinal);
            }
        }

        return text;
    }

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        var i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            n++;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }

        return n;
    }

    private static void CopyTree(string root, string backup, string outputDir)
    {
        Directory.CreateDirectory(backup);

        foreach (var file in Collect(root, outputDir))
        {
            var rel = Path.GetRelativePath(root, file);
            var dest = Path.Combine(backup, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
}
