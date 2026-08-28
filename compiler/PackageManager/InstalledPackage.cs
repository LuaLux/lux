using Nebra.Configuration;

namespace Nebra.PackageManager;

/// <summary>
/// A package that has been materialized into <c>nebra_modules/</c> of the current project.
/// Collected by <see cref="Discover"/> and consumed by compiler passes for auto-discovery
/// of types, annotation plugins and module code.
/// </summary>
public sealed record InstalledPackage(string Name, string RootPath, Config? Manifest)
{
    /// <summary>
    /// The package's own build output directory, as named by its manifest, or <c>null</c> when
    /// the package ships no manifest. Declaration files under it are generated from the package's
    /// own sources, so consuming them alongside those sources declares everything twice.
    /// </summary>
    public string? OutputRoot
    {
        get
        {
            if (Manifest is null || string.IsNullOrWhiteSpace(Manifest.Output)) return null;
            return Path.GetFullPath(Path.Combine(RootPath, Manifest.Output));
        }
    }

    /// <summary>
    /// The directories under the package root that should be scanned for annotation plugins.
    /// Empty means the package root itself.
    /// </summary>
    public IReadOnlyList<string> AnnotationRoots
    {
        get
        {
            if (Manifest is null) return [];
            return Manifest.Annotations
                .Select(entry => Path.IsPathRooted(entry) ? entry : Path.Combine(RootPath, entry))
                .ToList();
        }
    }
}

public static class InstalledPackages
{
    public const string CacheKey = "installed_packages";
    public const string ModulesDirName = "nebra_modules";

    /// <summary>
    /// Recursively enumerates files under <paramref name="root"/> without descending into a
    /// nested <c>nebra_modules/</c> or following a directory symlink.
    /// </summary>
    /// <remarks>
    /// A <c>file:</c> dependency is linked in as a symlink, so a package checkout that itself
    /// contains an installed copy of the consumer forms a cycle: walking it with
    /// <see cref="SearchOption.AllDirectories"/> never terminates. A dependency's own
    /// <c>nebra_modules/</c> is not the consumer's to read either - the installer flattens every
    /// transitive package into the consumer's own tree - so skipping it is what the resolver
    /// wants regardless of symlinks.
    /// </remarks>
    public static IEnumerable<string> EnumerateFilesSafely(string root, string pattern)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            string real;
            try { real = Path.GetFullPath(new DirectoryInfo(dir).LinkTarget ?? dir); }
            catch { real = dir; }
            if (!visited.Add(real)) continue;

            string[] files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch { continue; }
            foreach (var file in files) yield return file;

            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (string.Equals(name, ModulesDirName, StringComparison.OrdinalIgnoreCase)) continue;
                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Enumerates <c>nebra_modules/</c> under <paramref name="projectDir"/> and returns one
    /// <see cref="InstalledPackage"/> per direct child directory. Reads each package's
    /// <c>nebra.toml</c> when present; falls back to the directory name as the package name.
    /// </summary>
    public static List<InstalledPackage> Discover(string projectDir)
    {
        var result = new List<InstalledPackage>();
        var modulesDir = Path.Combine(projectDir, ModulesDirName);
        if (!Directory.Exists(modulesDir)) return result;

        foreach (var entry in Directory.EnumerateDirectories(modulesDir))
        {
            var dirName = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(dirName) || dirName.StartsWith('.')) continue;

            var manifestPath = Path.Combine(entry, "nebra.toml");
            Config? manifest = null;
            if (File.Exists(manifestPath))
            {
                manifest = Config.LoadFromFile(manifestPath);
            }

            var name = manifest?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = dirName;

            result.Add(new InstalledPackage(name!, entry, manifest));
        }

        return result;
    }
}
