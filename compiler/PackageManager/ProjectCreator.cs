using System.Net.Http;
using Nebra.Compiler;
using Nebra.Configuration;
using Nebra.Runtime;

namespace Nebra.PackageManager;

public sealed class CreateOptions
{
    public string? TargetDirectory { get; init; }
    public bool SkipSetup { get; init; }
    public bool NoRegistryCache { get; init; }
    public bool Offline { get; init; }
}

/// <summary>
/// Implements <c>nebra create &lt;spec&gt; [dir]</c>: resolves a template source —
/// either a direct URL pointing at a <c>setup.neb</c>/<c>setup.lua</c> file or a
/// package spec handled by the normal package manager pipeline — materialises it
/// into a fresh directory, then runs its setup script via <see cref="NebraRuntime"/>.
/// </summary>
public sealed class ProjectCreator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly GitFetcher _fetcher = new();

    public async Task<int> CreateAsync(string specString, CreateOptions opts)
    {
        if (string.IsNullOrWhiteSpace(specString))
        {
            await Console.Error.WriteLineAsync("error: missing template specifier. Usage: nebra create <spec_or_url> [dir]");
            return 1;
        }

        if (IsDirectScriptUrl(specString))
            return await CreateFromDirectUrlAsync(specString, opts);

        return await CreateFromPackageSpecAsync(specString, opts);
    }

    private static bool IsDirectScriptUrl(string spec)
    {
        if (!spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = spec.Split('?', '#')[0];
        return path.EndsWith(".neb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> CreateFromDirectUrlAsync(string url, CreateOptions opts)
    {
        var targetDir = opts.TargetDirectory ?? DeriveDirFromUrl(url);
        targetDir = Path.GetFullPath(targetDir);

        if (!EnsureEmptyDirectory(targetDir, out var err))
        {
            await Console.Error.WriteLineAsync($"error: {err}");
            return 1;
        }

        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "setup.neb";
        var tempDir = Path.Combine(Path.GetTempPath(), "nebra-create-url-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var setupPath = Path.Combine(tempDir, fileName);

        try
        {
            Console.WriteLine($"Downloading {url}...");
            var content = await Http.GetStringAsync(url);
            await File.WriteAllTextAsync(setupPath, content);

            Directory.CreateDirectory(targetDir);

            if (opts.SkipSetup)
            {
                File.Copy(setupPath, Path.Combine(targetDir, fileName));
                Console.WriteLine($"Setup script saved to '{targetDir}/{fileName}'. Setup skipped.");
                return 0;
            }

            return await RunSetupAsync(setupPath, targetDir);
        }
        catch (Exception ex) when (ex is not PackageManagerException)
        {
            await Console.Error.WriteLineAsync($"error: failed to download setup script: {ex.Message}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private async Task<int> CreateFromPackageSpecAsync(string specString, CreateOptions opts)
    {
        PackageSpec spec;
        try { spec = PackageSpec.Parse(specString); }
        catch (PackageManagerException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        if (spec.Kind == SpecKind.Alias)
        {
            if (opts.Offline)
            {
                await Console.Error.WriteLineAsync("error: alias resolution requires network access (disable --offline).");
                return 1;
            }
            try
            {
                var url = await Registry.ResolveAsync(spec.AliasName!, opts.NoRegistryCache);
                if (url == null)
                {
                    await Console.Error.WriteLineAsync($"error: alias '{spec.AliasName}' not found in registry index ({Registry.EffectiveUrl()}).");
                    return 1;
                }
                var combined = string.IsNullOrEmpty(spec.AliasRange) ? url : $"{url}#{spec.AliasRange}";
                spec = PackageSpec.Parse(combined);
            }
            catch (PackageManagerException ex)
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
                return 1;
            }
        }

        if (spec.Kind == SpecKind.File)
        {
            var src = Path.GetFullPath(spec.Path!);
            if (!Directory.Exists(src))
            {
                await Console.Error.WriteLineAsync($"error: file spec path does not exist: {src}");
                return 1;
            }

            var target = opts.TargetDirectory ?? new DirectoryInfo(src).Name;
            target = Path.GetFullPath(target);
            if (!EnsureEmptyDirectory(target, out var err))
            {
                await Console.Error.WriteLineAsync($"error: {err}");
                return 1;
            }
            return await PopulateTargetDirAsync(src, target, opts);
        }

        if (spec.Kind != SpecKind.Git)
        {
            await Console.Error.WriteLineAsync("error: unsupported spec type for `nebra create`.");
            return 1;
        }

        if (!await GitRunner.IsAvailableAsync())
        {
            await Console.Error.WriteLineAsync("error: 'git' not found on PATH. Install git and retry.");
            return 1;
        }

        var targetDir = opts.TargetDirectory ?? spec.Repo ?? "template";
        targetDir = Path.GetFullPath(targetDir);
        if (!EnsureEmptyDirectory(targetDir, out var dirErr))
        {
            await Console.Error.WriteLineAsync($"error: {dirErr}");
            return 1;
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), "nebra-create-stage-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(NebraHome.GitCacheRoot);

            Console.WriteLine($"Fetching {specString}...");
            var barePath = await _fetcher.EnsureBareCloneAsync(spec, opts.Offline);
            var commit = await ResolveGitRefAsync(barePath, spec);
            await _fetcher.EnsureSnapshotAsync(barePath, commit, stagingDir, spec.Subdir, forceFresh: true);

            return await PopulateTargetDirAsync(stagingDir, targetDir, opts);
        }
        catch (PackageManagerException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Final step of every `nebra create` flow. Receives the populated staging
    /// dir, decides whether to run a setup script or do a bulk copy, and
    /// leaves <paramref name="targetDir"/> containing only what the user
    /// actually wants:
    /// <list type="bullet">
    ///   <item><description>If staging has a <c>setup.neb</c>/<c>setup.lua</c>, run it with CWD=<paramref name="targetDir"/>
    ///     (which stays empty until the script writes files into it). The template's
    ///     own source files never land in the project.</description></item>
    ///   <item><description>If staging has no setup script, copy staging → target verbatim
    ///     (classic template-without-setup mode).</description></item>
    /// </list>
    /// </summary>
    private static async Task<int> PopulateTargetDirAsync(string stagingDir, string targetDir, CreateOptions opts)
    {
        var setupNebra = Path.Combine(stagingDir, "setup.neb");
        var setupLua = Path.Combine(stagingDir, "setup.lua");
        string? setupPath = File.Exists(setupNebra) ? setupNebra
                         : File.Exists(setupLua) ? setupLua
                         : null;

        Directory.CreateDirectory(targetDir);

        if (setupPath != null && !opts.SkipSetup)
        {
            return await RunSetupAsync(setupPath, targetDir);
        }

        CopyDirectory(stagingDir, targetDir);
        if (setupPath != null && opts.SkipSetup)
        {
            Console.WriteLine($"Template copied to '{targetDir}'. Setup skipped.");
        }
        return 0;
    }

    private static async Task<int> RunSetupAsync(string setupPath, string projectDir)
    {
        var originalCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = projectDir;

            string luaPath;
            string? tempCompileDir = null;

            if (setupPath.EndsWith(".neb", StringComparison.OrdinalIgnoreCase))
            {
                tempCompileDir = Path.Combine(Path.GetTempPath(), "nebra-setup-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempCompileDir);

                var compiler = new NebraCompiler { Config = new Config() };
                compiler.AddSource(setupPath);
                var ok = compiler.Compile();
                if (!ok)
                {
                    foreach (var diag in compiler.Diagnostics.Diagnostics)
                        await Console.Error.WriteLineAsync(diag.ToString());
                    await Console.Error.WriteLineAsync("error: failed to compile setup.neb.");
                    return 1;
                }

                luaPath = Path.Combine(tempCompileDir, "setup.lua");
                await WriteCompiledLuaAsync(compiler, setupPath, luaPath);
            }
            else
            {
                luaPath = setupPath;
            }

            using var runtime = new NebraRuntime();
            runtime.AddPackagePath(projectDir);
            var success = runtime.RunFile(luaPath);

            if (tempCompileDir != null)
            {
                try { Directory.Delete(tempCompileDir, recursive: true); } catch { }
            }

            if (!success) return 1;

            Console.WriteLine();
            Console.WriteLine($"\x1b[32m✓\x1b[0m Project created at {projectDir}");
            return 0;
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }

    private static async Task WriteCompiledLuaAsync(NebraCompiler compiler, string setupPath, string luaPath)
    {
        foreach (var pkg in compiler.Packages.Values)
        {
            foreach (var file in pkg.Files)
            {
                if (string.IsNullOrEmpty(file.GeneratedLua)) continue;
                if (!string.Equals(file.Filename, setupPath, StringComparison.OrdinalIgnoreCase)) continue;
                await File.WriteAllTextAsync(luaPath, file.GeneratedLua);
                return;
            }
        }

        foreach (var pkg in compiler.Packages.Values)
        {
            foreach (var file in pkg.Files)
            {
                if (string.IsNullOrEmpty(file.GeneratedLua)) continue;
                await File.WriteAllTextAsync(luaPath, file.GeneratedLua);
                return;
            }
        }

        throw new InvalidOperationException("setup.neb compiled to no output");
    }

    private async Task<string> ResolveGitRefAsync(string barePath, PackageSpec spec)
    {
        if (string.IsNullOrEmpty(spec.Ref))
            return await _fetcher.ResolveDefaultBranchAsync(barePath);

        if (SemVerRange.TryParse(spec.Ref, out var range) && range is not null
            && range.Kind != SemVerRangeKind.Exact)
        {
            var tags = await _fetcher.ListTagsAsync(barePath);
            var candidates = new List<(SemVer Ver, string Tag)>();
            foreach (var tag in tags)
                if (SemVer.TryParse(tag, out var v) && v is not null) candidates.Add((v, tag));
            var best = range.PickBest(candidates.Select(c => c.Ver));
            if (best is null)
                throw new PackageManagerException($"no tag matches range '{spec.Ref}' for {spec.CloneUrl}");
            var bestTag = candidates.First(c => c.Ver == best).Tag;
            return await _fetcher.ResolveRefAsync(barePath, bestTag);
        }

        return await _fetcher.ResolveRefAsync(barePath, spec.Ref);
    }

    private static bool EnsureEmptyDirectory(string path, out string error)
    {
        error = "";
        if (Directory.Exists(path))
        {
            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                error = $"target directory '{path}' already exists and is not empty.";
                return false;
            }
            return true;
        }

        if (File.Exists(path))
        {
            error = $"target path '{path}' exists as a file.";
            return false;
        }

        Directory.CreateDirectory(path);
        return true;
    }

    private static string DeriveDirFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                var seg = segments[i];
                if (seg.EndsWith(".neb", StringComparison.OrdinalIgnoreCase) ||
                    seg.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    continue;
                return seg;
            }
            if (segments.Length >= 2) return segments[^2];
        }
        catch { }
        return "nebra-template";
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "nebra_modules" or "out") continue;
            CopyDirectory(dir, Path.Combine(destination, name));
        }
    }
}
