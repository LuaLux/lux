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
