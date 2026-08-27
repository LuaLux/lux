using Nebra.Configuration;

namespace Nebra.PackageManager;

public sealed class InstallOptions
{
    public bool IncludeDev { get; init; } = true;
    public bool Frozen { get; init; } = false;
    public bool Offline { get; init; } = false;

    /// <summary>Bypass the cached registry index and force a refresh from the network.</summary>
    public bool NoRegistryCache { get; init; } = false;

    /// <summary>
    /// When true, lifecycle scripts (<c>preinstall</c>/<c>postinstall</c>) of every fetched
    /// dependency are executed. Mutually exclusive with <see cref="AllowedScriptPackages"/>;
    /// if both are set, the latter wins.
    /// </summary>
    public bool AllowAllScripts { get; init; } = false;

    /// <summary>
    /// Allow-list of package names whose lifecycle scripts may run. Empty means none.
    /// Ignored when <see cref="AllowAllScripts"/> is true and this set is empty.
    /// </summary>
    public HashSet<string> AllowedScriptPackages { get; init; } = new(StringComparer.Ordinal);

    internal bool IsScriptAllowed(string pkgName)
    {
        if (AllowedScriptPackages.Count > 0)
            return AllowedScriptPackages.Contains(pkgName);
        return AllowAllScripts;
    }
}

/// <summary>
/// Top-level orchestrator for <c>nebra install</c>. Resolves the dependency graph starting
/// from the project manifest, fetches each package into the global store, writes the
/// lockfile and links everything into <c>nebra_modules/</c>.
/// </summary>
public sealed class Installer
{
    private readonly GitFetcher _fetcher = new();

    public async Task<int> InstallAsync(Config config, string projectDir, InstallOptions opts)
    {
        if (!await GitRunner.IsAvailableAsync())
        {
            await Console.Error.WriteLineAsync("error: 'git' not found on PATH. Install git and retry.");
            return 1;
        }

        Directory.CreateDirectory(NebraHome.StoreRoot);
        Directory.CreateDirectory(NebraHome.GitCacheRoot);

        var lockPath = Path.Combine(projectDir, "nebra.lock");
        var modulesDir = Path.Combine(projectDir, "nebra_modules");
        var existing = Lockfile.Load(lockPath);

        var roots = BuildRootSet(config, opts.IncludeDev);
        if (roots.Count == 0)
        {
            Directory.CreateDirectory(modulesDir);
            PruneStaleLinks(modulesDir, Array.Empty<string>());
            new Lockfile { Version = 1 }.Save(lockPath);
            Console.WriteLine("No dependencies.");
            return 0;
        }

        var resolved = new Dictionary<string, LockedPackage>(StringComparer.Ordinal);
        var queue = new Queue<(string Name, PackageSpec Spec, string Parent)>();
        foreach (var (n, s) in roots) queue.Enqueue((n, s, "<root>"));

        while (queue.TryDequeue(out var item))
        {
            var (name, spec, parent) = item;

            if (resolved.TryGetValue(name, out var already))
            {
                if (already.Spec != spec.Raw)
                    await Console.Error.WriteLineAsync(
                        $"warning: '{name}' requested twice with different specs ('{already.Spec}' vs '{spec.Raw}'). " +
                        "Keeping first. Add a scoped alias (e.g. \"@you/" + name + "\" = ...) in nebra.toml to disambiguate.");
                continue;
            }

            LockedPackage locked;
            using var spinner = new Spinner($"Resolving {name}...");
            try
            {
                locked = await ResolveOneAsync(name, spec, existing, opts);
            }
            catch (PackageManagerException ex)
            {
                spinner.Stop();
                await Console.Error.WriteLineAsync($"error: failed to install '{name}' (required by {parent}): {ex.Message}");
                return 1;
            }

            resolved[name] = locked;
            spinner.Stop($"  resolved {name} -> {locked.Resolved}@{Short(locked.Commit)}{(locked.Version is null ? "" : " (" + locked.Version + ")")}");

            var storePath = StorePathOf(locked);
            var depManifestPath = Path.Combine(storePath, "nebra.toml");
            if (File.Exists(depManifestPath))
            {
                var depCfg = Config.LoadFromFile(depManifestPath);
                if (depCfg != null)
                {
                    foreach (var (dn, dv) in depCfg.Dependencies)
                    {
                        if (!locked.Deps.Contains(dn)) locked.Deps.Add(dn);
                        if (!resolved.ContainsKey(dn))
                        {
                            PackageSpec childSpec;
                            try { childSpec = PackageSpec.FromValue(dv); }
                            catch (PackageManagerException ex)
                            {
                                await Console.Error.WriteLineAsync($"error: invalid dependency '{dn}' in package '{name}': {ex.Message}");
                                return 1;
                            }
                            queue.Enqueue((dn, childSpec, name));
                        }
                    }
                }
            }
        }

        var newLock = new Lockfile
        {
            Version = 1,
            Packages = resolved.Values.ToList(),
        };
        newLock.Save(lockPath);

        Directory.CreateDirectory(modulesDir);
        PruneStaleLinks(modulesDir, resolved.Keys);

        var scriptDeniedNotices = new List<string>();
        var allowAnyScripts = opts.AllowAllScripts || opts.AllowedScriptPackages.Count > 0;

        foreach (var locked in resolved.Values)
        {
            var storePath = StorePathOf(locked);
            var linkPath = Path.Combine(modulesDir, locked.Name);

            var depCfg = LoadDepManifest(storePath);
            if (depCfg != null && (depCfg.Scripts.PreInstall.Count > 0 || depCfg.Scripts.PostInstall.Count > 0))
            {
                if (opts.IsScriptAllowed(locked.Name))
                {
                    if (!RunPackageScripts(locked.Name, "preinstall", depCfg.Scripts.PreInstall, storePath))
                        return 1;
                }
                else if (depCfg.Scripts.PreInstall.Count > 0)
                {
                    scriptDeniedNotices.Add($"  {locked.Name}: preinstall ({depCfg.Scripts.PreInstall.Count} cmd)");
                }
            }

            using (var linkSpinner = new Spinner($"Linking {locked.Name}..."))
            {
                Linker.Link(storePath, linkPath, config.Install.Linker);
            }

            if (depCfg != null && depCfg.Scripts.PostInstall.Count > 0)
            {
                if (opts.IsScriptAllowed(locked.Name))
                {
                    if (!RunPackageScripts(locked.Name, "postinstall", depCfg.Scripts.PostInstall, linkPath))
                        return 1;
                }
                else
                {
                    scriptDeniedNotices.Add($"  {locked.Name}: postinstall ({depCfg.Scripts.PostInstall.Count} cmd)");
                }
            }
        }

        EnsureGitignore(projectDir);

        if (scriptDeniedNotices.Count > 0 && !allowAnyScripts)
        {
            Console.WriteLine();
            Console.WriteLine("warning: the following packages declared lifecycle scripts that were skipped");
            foreach (var line in scriptDeniedNotices) Console.WriteLine(line);
            Console.WriteLine("Run with `--allow-scripts` (or `--allow-scripts=pkg1,pkg2`) to enable.");
        }
        else if (scriptDeniedNotices.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("warning: lifecycle scripts skipped for packages not in --allow-scripts list:");
            foreach (var line in scriptDeniedNotices) Console.WriteLine(line);
        }

        Console.WriteLine($"Installed {resolved.Count} package(s) into nebra_modules/.");
        return 0;
    }

    private static Config? LoadDepManifest(string storePath)
    {
        var manifestPath = Path.Combine(storePath, "nebra.toml");
        return File.Exists(manifestPath) ? Config.LoadFromFile(manifestPath) : null;
    }

    private static bool RunPackageScripts(string pkgName, string phase, List<string> scripts, string workingDir)
    {
        foreach (var script in scripts)
        {
            Console.WriteLine($"[{pkgName}:{phase}] {script}");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd" : "sh",
                Arguments = OperatingSystem.IsWindows() ? $"/c {script}" : $"-c \"{script}\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                Console.Error.WriteLine($"error: could not start '{phase}' script for {pkgName}");
                return false;
            }
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"error: {pkgName}:{phase} exited with code {proc.ExitCode}");
                return false;
            }
        }
        return true;
    }

    public async Task<int> AddAsync(string specString, Config config, string projectDir,
        string configPath, DependencyGroup group, InstallOptions opts)
    {
        PackageSpec spec;
        try { spec = PackageSpec.Parse(specString); }
        catch (PackageManagerException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        // We need the package name. For git/file specs, read the manifest after fetching.
        string? declaredName = null;
        var aliasName = spec.Kind == SpecKind.Alias ? spec.AliasName : null;
        if (spec.Kind == SpecKind.Alias)
        {
            try
            {
                spec = await ResolveAliasAsync(spec, opts);
            }
            catch (PackageManagerException ex)
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
                return 1;
            }
        }

        if (spec.Kind == SpecKind.Git)
        {
            try
            {
                var locked = await ResolveOneAsync("<pending>", spec, null, opts);
                var manifestPath = Path.Combine(StorePathOf(locked), "nebra.toml");
                var cfg = File.Exists(manifestPath) ? Config.LoadFromFile(manifestPath) : null;
                declaredName = cfg?.Name;
            }
            catch (PackageManagerException ex)
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
                return 1;
            }
        }
        else if (spec.Kind == SpecKind.File)
        {
            var manifestPath = Path.Combine(Path.GetFullPath(spec.Path!), "nebra.toml");
            var cfg = File.Exists(manifestPath) ? Config.LoadFromFile(manifestPath) : null;
            declaredName = cfg?.Name;
        }

        if (string.IsNullOrWhiteSpace(declaredName))
        {
            declaredName = aliasName ?? spec.Kind switch
            {
                SpecKind.Git => spec.Repo,
                SpecKind.File => new DirectoryInfo(Path.GetFullPath(spec.Path!)).Name,
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(declaredName))
        {
            await Console.Error.WriteLineAsync("error: cannot determine package name.");
            return 1;
        }

        var targetMap = group switch
        {
            DependencyGroup.Dev => config.DevDependencies,
            DependencyGroup.Peer => config.PeerDependencies,
            _ => config.Dependencies
        };
        targetMap[declaredName] = specString;

        if (!ManifestEditor.AddOrUpdate(configPath, group, declaredName, specString))
        {
            await Console.Error.WriteLineAsync("error: could not update nebra.toml (parse error or missing file)");
            return 1;
        }

        Console.WriteLine($"Added {declaredName} -> {specString}");
        return await InstallAsync(config, projectDir, opts);
    }

    public async Task<int> RemoveAsync(string name, Config config, string projectDir, string configPath, InstallOptions opts)
    {
        // Direct match against the dep key — what gets stored after `nebra add`
        // is the package's canonical name from its manifest, NOT what the user
        // typed. So `nebra add my-alias` → entry "actual-pkg = "my-alias"`, and
        // `nebra remove my-alias` would miss without the value-match fallback
        // below.
        var actualKey = ResolveDepKey(config, name);
        if (actualKey == null)
        {
            await Console.Error.WriteLineAsync($"error: '{name}' is not a declared dependency.");
            return 1;
        }

        config.Dependencies.Remove(actualKey);
        config.DevDependencies.Remove(actualKey);
        config.PeerDependencies.Remove(actualKey);

        if (!ManifestEditor.Remove(configPath, actualKey))
        {
            await Console.Error.WriteLineAsync("warning: nebra.toml was not updated (entry not present in file).");
        }

        Console.WriteLine(actualKey == name
            ? $"Removed {name}"
            : $"Removed {actualKey} (added as '{name}')");
        return await InstallAsync(config, projectDir, opts);
    }

    /// <summary>
    /// Finds which dependency entry matches <paramref name="userInput"/> across
    /// all dep groups. Tries the key first (canonical name), then matches the
    /// raw spec value — so users can remove via the alias they originally
    /// passed to <c>nebra add</c>. Returns null if nothing matches; returns the
    /// canonical key on success.
    /// </summary>
    private static string? ResolveDepKey(Config config, string userInput)
    {
        foreach (var map in new[] { config.Dependencies, config.DevDependencies, config.PeerDependencies })
        {
            if (map.ContainsKey(userInput)) return userInput;
        }
        foreach (var map in new[] { config.Dependencies, config.DevDependencies, config.PeerDependencies })
        {
            foreach (var (key, val) in map)
            {
                if (val is string s && string.Equals(s, userInput, StringComparison.Ordinal))
                    return key;
            }
        }
        return null;
    }

    private Dictionary<string, PackageSpec> BuildRootSet(Config config, bool includeDev)
    {
        var result = new Dictionary<string, PackageSpec>(StringComparer.Ordinal);
        foreach (var (name, value) in config.Dependencies)
            result[name] = PackageSpec.FromValue(value);
        if (includeDev)
        {
            foreach (var (name, value) in config.DevDependencies)
                result.TryAdd(name, PackageSpec.FromValue(value));
        }
        return result;
    }

    private async Task<LockedPackage> ResolveOneAsync(string name, PackageSpec spec, Lockfile? existing, InstallOptions opts)
    {
        if (spec.Kind == SpecKind.File)
            return ResolveFileSpec(name, spec);

        if (spec.Kind == SpecKind.Alias)
            spec = await ResolveAliasAsync(spec, opts);

        // git
        var existingMatch = existing?.Packages.FirstOrDefault(p => p.Name == name && p.Spec == spec.Raw);
        if (existingMatch != null && opts.Frozen)
        {
            await EnsureStorePopulatedAsync(existingMatch, spec);
            return new LockedPackage
            {
                Name = name,
                Spec = existingMatch.Spec,
                Resolved = existingMatch.Resolved,
                Commit = existingMatch.Commit,
                Version = existingMatch.Version,
                Integrity = existingMatch.Integrity,
                Subdir = existingMatch.Subdir,
            };
        }

        if (opts.Offline)
            throw new PackageManagerException($"offline mode: '{name}' not found in lock or store");

        var barePath = await _fetcher.EnsureBareCloneAsync(spec);
        var (commit, versionTag) = await ResolveGitRefAsync(barePath, spec);

        var storePath = NebraHome.PackagePath(spec.Host!, spec.Owner!, spec.Repo!, commit);
        await _fetcher.EnsureSnapshotAsync(barePath, commit, storePath, spec.Subdir);

        var integrity = GitFetcher.ComputeIntegrity(storePath);

        // If an existing entry has the same commit, the integrity must match.
        if (existingMatch != null && existingMatch.Commit == commit
            && !string.IsNullOrEmpty(existingMatch.Integrity)
            && existingMatch.Integrity != integrity)
        {
            throw new PackageManagerException(
                $"integrity mismatch for {name}@{commit[..8]}: expected {existingMatch.Integrity}, got {integrity}");
        }

        return new LockedPackage
        {
            Name = name,
            Spec = spec.Raw,
            Resolved = $"{spec.Host}:{spec.Owner}/{spec.Repo}",
            Commit = commit,
            Version = versionTag,
            Integrity = integrity,
            Subdir = spec.Subdir,
        };
    }

    private static async Task<PackageSpec> ResolveAliasAsync(PackageSpec spec, InstallOptions opts)
    {
        if (opts.Offline)
            throw new PackageManagerException(
                $"alias '{spec.AliasName}' cannot be resolved in offline mode (registry lookup disabled)");

        var url = await Registry.ResolveAsync(spec.AliasName!, opts.NoRegistryCache);
        if (url is null)
            throw new PackageManagerException(
                $"alias '{spec.AliasName}' not found in registry index ({Registry.EffectiveUrl()}). " +
                "Use an explicit git spec (e.g. github:owner/repo) instead.");

        var combined = string.IsNullOrEmpty(spec.AliasRange) ? url : $"{url}#{spec.AliasRange}";
        var resolved = PackageSpec.Parse(combined);
        if (resolved.Kind != SpecKind.Git)
            throw new PackageManagerException(
                $"alias '{spec.AliasName}' resolved to '{url}' which is not a git URL");
        return resolved;
    }

    private LockedPackage ResolveFileSpec(string name, PackageSpec spec)
    {
        var path = Path.GetFullPath(spec.Path!);
        if (!Directory.Exists(path))
            throw new PackageManagerException($"file spec path does not exist: {path}");
        var integrity = GitFetcher.ComputeIntegrity(path);
        return new LockedPackage
        {
            Name = name,
            Spec = spec.Raw,
            Resolved = "file:" + path,
            Commit = "",
            Version = null,
            Integrity = integrity,
        };
    }

    private async Task<(string Commit, string? Version)> ResolveGitRefAsync(string barePath, PackageSpec spec)
    {
        if (string.IsNullOrEmpty(spec.Ref))
        {
            var commit = await _fetcher.ResolveDefaultBranchAsync(barePath);
            return (commit, null);
        }

        if (SemVerRange.TryParse(spec.Ref, out var range) && range is not null
            && range.Kind != SemVerRangeKind.Exact)
        {
            var tags = await _fetcher.ListTagsAsync(barePath);
            var candidates = new List<(SemVer Ver, string Tag)>();
            foreach (var tag in tags)
            {
                if (SemVer.TryParse(tag, out var v) && v is not null)
                    candidates.Add((v, tag));
            }
            var best = range.PickBest(candidates.Select(c => c.Ver));
            if (best is null)
                throw new PackageManagerException($"no tag matches range '{spec.Ref}' for {spec.CloneUrl}");
            var bestTag = candidates.First(c => c.Ver == best).Tag;
            var commit = await _fetcher.ResolveRefAsync(barePath, bestTag);
            return (commit, best.Raw);
        }

        // Exact semver or arbitrary ref — try ref as-is, fall back to tag, fall back to branch.
        var resolved = await _fetcher.ResolveRefAsync(barePath, spec.Ref);
        string? versionTag = null;
        if (SemVer.TryParse(spec.Ref, out var asVer) && asVer is not null)
            versionTag = asVer.Raw;
        return (resolved, versionTag);
    }

    /// <summary>
    /// Ensures the store holds a snapshot of the locked commit. The bare clone is refreshed
    /// first; a commit still missing afterwards means the cache entry is stale, which is
    /// reported as such instead of surfacing as a raw <c>git archive</c> failure.
    /// </summary>
    private async Task EnsureStorePopulatedAsync(LockedPackage locked, PackageSpec spec)
    {
        if (spec.Kind != SpecKind.Git) return;
        var storePath = NebraHome.PackagePath(spec.Host!, spec.Owner!, spec.Repo!, locked.Commit);
        if (Directory.Exists(storePath) && Directory.EnumerateFileSystemEntries(storePath).Any())
            return;

        var barePath = await _fetcher.EnsureBareCloneAsync(spec);

        if (!await GitFetcher.CommitExistsAsync(barePath, locked.Commit))
        {
            var shortCommit = locked.Commit.Length > 8 ? locked.Commit[..8] : locked.Commit;
            throw new PackageManagerException(
                $"commit {shortCommit} of '{locked.Name}' is missing from the cached clone of {spec.CloneUrl} " +
                "after fetching. The cache entry looks stale: run 'nebra pm prune' and install again.");
        }

        await _fetcher.EnsureSnapshotAsync(barePath, locked.Commit, storePath, spec.Subdir);
    }

    private static string StorePathOf(LockedPackage locked)
    {
        if (locked.Resolved.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return locked.Resolved[5..];

        // "<host>:<owner>/<repo>"
        var colon = locked.Resolved.IndexOf(':');
        if (colon < 0)
            throw new PackageManagerException($"invalid 'resolved' in lock: {locked.Resolved}");
        var host = locked.Resolved[..colon];
        var rest = locked.Resolved[(colon + 1)..];
        var slash = rest.IndexOf('/');
        var owner = rest[..slash];
        var repo = rest[(slash + 1)..];
        return NebraHome.PackagePath(host, owner, repo, locked.Commit);
    }

    private static void PruneStaleLinks(string modulesDir, IEnumerable<string> keep)
    {
        if (!Directory.Exists(modulesDir)) return;
        var keepSet = new HashSet<string>(keep, StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(modulesDir))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue;
            if (keepSet.Contains(name)) continue;
            try
            {
                var info = new DirectoryInfo(entry);
                if (info.Exists)
                {
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) info.Delete();
                    else info.Delete(recursive: true);
                }
                else if (File.Exists(entry)) File.Delete(entry);
            }
            catch { /* best effort */ }
        }
    }

    private static void EnsureGitignore(string projectDir)
    {
        var gi = Path.Combine(projectDir, ".gitignore");
        const string line = "nebra_modules/";
        if (!File.Exists(gi))
        {
            File.WriteAllText(gi, line + "\n");
            return;
        }
        var content = File.ReadAllText(gi);
        if (content.Contains("nebra_modules/", StringComparison.Ordinal) ||
            content.Contains("nebra_modules", StringComparison.Ordinal))
            return;
        if (!content.EndsWith('\n')) content += "\n";
        content += line + "\n";
        File.WriteAllText(gi, content);
    }

    private static string Short(string sha) => sha.Length >= 8 ? sha[..8] : sha;
}

public enum DependencyGroup
{
    Runtime,
    Dev,
    Peer,
}
