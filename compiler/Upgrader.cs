using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nebra;

internal static class Upgrader
{
    private const string GithubReleasesLatestUrl = "https://api.github.com/repos/nebra-lang/nebra/releases/latest";
    private const string UserAgent = "nebra-cli";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(2);

    public static async Task<int> RunUpgradeAsync(string[] args)
    {
        var force = args.Contains("--force") || args.Contains("-f");

        Console.WriteLine("Checking for updates...");
        GitHubRelease? release;
        try
        {
            release = await FetchLatestReleaseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to query GitHub releases: {ex.Message}");
            return 1;
        }

        if (release == null || string.IsNullOrEmpty(release.TagName))
        {
            await Console.Error.WriteLineAsync("No releases found on GitHub.");
            return 1;
        }

        var current = Program.GetNebraVersion();
        var latest = release.TagName.TrimStart('v');

        WriteCache(latest);

        if (!force && CompareSemver(current, latest) >= 0)
        {
            Console.WriteLine($"nebra is already at the latest version ({current}).");
            return 0;
        }

        var assetName = PickAssetForPlatform();
        if (assetName == null)
        {
            await Console.Error.WriteLineAsync(
                $"No prebuilt release asset available for {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). Build from source instead.");
            return 1;
        }

        var asset = release.Assets?.FirstOrDefault(a => a.Name == assetName);
        if (asset == null)
        {
            await Console.Error.WriteLineAsync($"Release '{release.TagName}' has no asset named '{assetName}'.");
            return 1;
        }

        Console.WriteLine($"Current: {current}");
        Console.WriteLine($"Latest:  {latest}");
        Console.WriteLine($"Downloading {assetName}...");

        var tempArchive = Path.Combine(Path.GetTempPath(), $"nebra-upgrade-{Guid.NewGuid():N}{(assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(assetName))}");
        var tempDir = Path.Combine(Path.GetTempPath(), $"nebra-upgrade-{Guid.NewGuid():N}");
        try
        {
            await DownloadAsync(asset.BrowserDownloadUrl, tempArchive);

            Console.WriteLine("Extracting...");
            Directory.CreateDirectory(tempDir);
            await ExtractAsync(tempArchive, tempDir);

            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var binaryName = isWindows ? "nebra.exe" : "nebra";
            var newBinary = FindBinary(tempDir, binaryName);
            if (newBinary == null)
            {
                await Console.Error.WriteLineAsync($"Extracted archive does not contain '{binaryName}'.");
                return 1;
            }

            var currentPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentPath))
            {
                await Console.Error.WriteLineAsync("Could not determine the current executable path.");
                return 1;
            }

            Console.WriteLine($"Installing to {currentPath}...");
            try
            {
                ReplaceBinary(currentPath, newBinary);
            }
            catch (UnauthorizedAccessException)
            {
                var hint = isWindows
                    ? "Try again from an Administrator shell."
                    : "Try: sudo nebra upgrade  (or reinstall nebra to a user-writable path).";
                await Console.Error.WriteLineAsync($"Permission denied writing to {currentPath}. {hint}");
                return 1;
            }
            catch (Exception inPlaceEx)
            {
                Console.WriteLine($"In-place replace failed ({inPlaceEx.GetType().Name}: {inPlaceEx.Message}).");
                Console.WriteLine("Scheduling deferred upgrade — a helper will apply it once this process exits.");
                SpawnDeferredHelper(currentPath, newBinary);
                Console.WriteLine($"Update will land at {currentPath} shortly. Run `nebra version` afterwards to confirm.");
                return 0;
            }

            Console.WriteLine($"Updated nebra: {current} -> {latest}");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Upgrade failed: {ex.Message}");
            return 1;
        }
        finally
        {
            try { if (File.Exists(tempArchive)) File.Delete(tempArchive); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static async Task<int> RunCheckAsync()
    {
        var current = Program.GetNebraVersion();
        Console.WriteLine($"Current: {current}");

        GitHubRelease? release;
        try
        {
            release = await FetchLatestReleaseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to query GitHub releases: {ex.Message}");
            return 1;
        }

        if (release == null || string.IsNullOrEmpty(release.TagName))
        {
            await Console.Error.WriteLineAsync("No releases found.");
            return 1;
        }

        var latest = release.TagName.TrimStart('v');
        WriteCache(latest);

        Console.WriteLine($"Latest:  {latest}");
        if (CompareSemver(current, latest) >= 0)
        {
            Console.WriteLine("nebra is up to date.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("A newer version is available. Run `nebra upgrade` to install it.");
        return 0;
    }

    public static async Task MaybeShowUpdateNoticeAsync()
    {
        try
        {
            var cache = ReadCache();
            var current = Program.GetNebraVersion();

            if (cache != null && DateTimeOffset.UtcNow - cache.CheckedAt < CacheTtl)
            {
                EmitNotice(current, cache.LatestVersion);
                return;
            }

            using var cts = new CancellationTokenSource(NotifyTimeout);
            try
            {
                var release = await FetchLatestReleaseAsync(cts.Token);
                if (release == null || string.IsNullOrEmpty(release.TagName)) return;
                var latest = release.TagName.TrimStart('v');
                WriteCache(latest);
                EmitNotice(current, latest);
            }
            catch
            {
                // Offline / rate-limited — stay silent.
            }
        }
        catch
        {
            // Never let the notice break the calling command.
        }
    }

    /// <summary>
    /// Notfall-Pfad. Wird aufgerufen wenn <see cref="ReplaceBinary"/> trotz aller
    /// Vorsicht knallt (z.B. weil das Ziel als Text-Segment gemappt ist und das
    /// Filesystem keine atomare Replace-Operation hergibt). Wir kopieren das neue
    /// Binary in einen unabhängigen Temp-Ordner und starten es als
    /// <c>_upgrade_apply</c>-Helper. Der wartet auf das Sterben des aktuellen
    /// Prozesses, kopiert dann das (ebenfalls gestagete) Binary über das Ziel
    /// und räumt sich selbst auf. Wichtig: der Helper darf NICHT aus dem Pfad
    /// laufen den er gleich überschreibt — sonst trifft ihn dasselbe ETXTBSY.
    /// </summary>
    private static void SpawnDeferredHelper(string currentPath, string newBinary)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var helperDir = Path.Combine(Path.GetTempPath(), $"nebra-upgrade-helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(helperDir);

        var helperBinaryName = isWindows ? "nebra-helper.exe" : "nebra-helper";
        var helperBinary = Path.Combine(helperDir, helperBinaryName);
        var stagedSource = Path.Combine(helperDir, isWindows ? "nebra.new.exe" : "nebra.new");
        var logPath = Path.Combine(helperDir, "log.txt");

        File.Copy(newBinary, helperBinary, overwrite: true);
        File.Copy(newBinary, stagedSource, overwrite: true);

        if (!isWindows)
        {
            var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(helperBinary, execMode);
            File.SetUnixFileMode(stagedSource, execMode);
        }

        var psi = new ProcessStartInfo
        {
            FileName = helperBinary,
            UseShellExecute = false,
            CreateNoWindow = true,
            // No stdin/stdout/stderr inheritance — once the parent exits those
            // handles close and writes would fail. The helper logs to log.txt
            // inside its own dir for debugging instead.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add("_upgrade_apply");
        psi.ArgumentList.Add("--pid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(stagedSource);
        psi.ArgumentList.Add("--target");
        psi.ArgumentList.Add(currentPath);
        psi.ArgumentList.Add("--cleanup-dir");
        psi.ArgumentList.Add(helperDir);
        psi.ArgumentList.Add("--log");
        psi.ArgumentList.Add(logPath);

        Process.Start(psi);
        // Intentionally don't wait. Helper continues independently after this
        // process exits; on Unix init reaps it, on Windows the OS handles it.
    }

    /// <summary>
    /// Helper-Entry-Point. Wartet bis der aufrufende nebra-Prozess weg ist, dann
    /// überschreibt das Ziel mit der Staged-Source. Mehrere Retries weil
    /// andere nebra-Prozesse (z.B. ein parallel laufender <c>nebra lps</c> im VS
    /// Code) das Ziel kurzzeitig noch mappen können.
    /// </summary>
    public static async Task<int> RunDeferredApplyAsync(string[] args)
    {
        int? pid = null;
        string? source = null;
        string? target = null;
        string? cleanupDir = null;
        string? logPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var p)) pid = p;
                    break;
                case "--source" when i + 1 < args.Length:
                    source = args[++i]; break;
                case "--target" when i + 1 < args.Length:
                    target = args[++i]; break;
                case "--cleanup-dir" when i + 1 < args.Length:
                    cleanupDir = args[++i]; break;
                case "--log" when i + 1 < args.Length:
                    logPath = args[++i]; break;
            }
        }

        if (source == null || target == null)
        {
            Log(logPath, "missing required --source or --target");
            return 1;
        }

        if (pid is { } parentPid)
        {
            Log(logPath, $"waiting for parent pid {parentPid}...");
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                await parent.WaitForExitAsync();
            }
            catch (ArgumentException)
            {
                // Parent already gone — fine, fall through to the replace.
            }
            catch (Exception ex)
            {
                Log(logPath, $"wait error: {ex.Message} — proceeding anyway");
            }
        }

        // Grace period: even after the parent is "exited", the kernel may need
        // a tick to fully unmap the text segment. 500ms is generous.
        await Task.Delay(500);

        Log(logPath, $"applying {source} -> {target}");

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetUnixFileMode(target,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                Log(logPath, $"success on attempt {attempt}");
                TryRemoveDir(cleanupDir);
                return 0;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log(logPath, $"attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
            }
        }

        Log(logPath, $"giving up after 10 attempts. last error: {lastError}");
        // Don't clean up the dir on failure — keeps the log around for the
        // user to inspect.
        return 1;
    }

    private static void Log(string? path, string message)
    {
        if (path == null) return;
        try
        {
            File.AppendAllText(path, $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

    private static void TryRemoveDir(string? path)
    {
        if (path == null) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    public static void CleanupStaleBackup()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return;
        var old = current + ".old";
        if (!File.Exists(old)) return;
        try { File.Delete(old); } catch { /* still locked from a sibling invocation; retry next time */ }
    }

    private static void EmitNotice(string current, string latest)
    {
        if (CompareSemver(current, latest) >= 0) return;
        Console.WriteLine();
        Console.WriteLine($"A new version of nebra is available: {current} -> {latest}");
        Console.WriteLine("Run `nebra upgrade` to update.");
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.GetAsync(GithubReleasesLatestUrl, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: ct);
    }

    private static async Task DownloadAsync(string url, string destPath)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength;
        var canRender = !Console.IsOutputRedirected;

        await using var contentStream = await resp.Content.ReadAsStreamAsync();
        await using var fs = File.Create(destPath);

        var buffer = new byte[81920];
        long readBytes = 0;
        var lastRender = DateTime.UtcNow - TimeSpan.FromSeconds(1);

        int n;
        while ((n = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n));
            readBytes += n;

            if (canRender && (DateTime.UtcNow - lastRender).TotalMilliseconds >= 100)
            {
                RenderProgress(readBytes, totalBytes);
                lastRender = DateTime.UtcNow;
            }
        }

        if (canRender)
        {
            RenderProgress(readBytes, totalBytes);
            Console.WriteLine();
        }
    }

    private static void RenderProgress(long current, long? total)
    {
        const int width = 30;
        if (total is { } t && t > 0)
        {
            var pct = Math.Clamp((double)current / t, 0.0, 1.0);
            var filled = (int)Math.Round(pct * width);
            var bar = new string('█', filled) + new string('░', width - filled);
            Console.Write($"\r  [{bar}] {pct,4:P0}  {FormatBytes(current)} / {FormatBytes(t)}   ");
        }
        else
        {
            Console.Write($"\r  {FormatBytes(current)} downloaded   ");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MiB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GiB";
    }

    private static async Task ExtractAsync(string archivePath, string destDir)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
            return;
        }
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var fs = File.OpenRead(archivePath);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, destDir, overwriteFiles: true);
            return;
        }
        throw new NotSupportedException($"Unsupported archive format: {archivePath}");
    }

    private static string? FindBinary(string root, string name)
    {
        var direct = Path.Combine(root, name);
        if (File.Exists(direct)) return direct;
        foreach (var f in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
            return f;
        return null;
    }

    private static string? PickAssetForPlatform()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (arch == null) return null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"nebra-linux-{arch}.tar.gz";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return arch == "arm64" ? "nebra-osx-arm64.tar.gz" : null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"nebra-win-{arch}.zip";
        return null;
    }

    private static void ReplaceBinary(string currentPath, string newBinary)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows locks running .exe files: overwrite/delete are forbidden,
            // but rename of the running .exe is allowed. Move current out of the
            // way, drop the new binary in. The .old file is removed on the next
            // nebra invocation (see CleanupStaleBackup).
            var oldPath = currentPath + ".old";
            if (File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { }
            }
            File.Move(currentPath, oldPath);
            try
            {
                File.Move(newBinary, currentPath);
            }
            catch
            {
                try { File.Move(oldPath, currentPath); } catch { }
                throw;
            }
        }
        else
        {
            // Unix: replacing a running binary via rename(2) is safe (kernel
            // keeps the old inode mapped for the running process). But if the
            // staged file lives on a different filesystem than the target
            // (e.g. extracted to /tmp which is tmpfs while the binary lives
            // under $HOME on the root fs), File.Move falls back to copy+
            // delete and the copy hits ETXTBSY because it opens the running
            // exe for writing. Stage a sibling of the target so the final
            // move is a same-fs rename.
            var stagedPath = currentPath + ".new";
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); } catch { }
            File.Copy(newBinary, stagedPath, overwrite: true);
            File.SetUnixFileMode(stagedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Move(stagedPath, currentPath, overwrite: true);
        }
    }

    private static int CompareSemver(string a, string b)
    {
        var ad = a.IndexOf('-');
        var bd = b.IndexOf('-');
        var aCore = ad >= 0 ? a[..ad] : a;
        var bCore = bd >= 0 ? b[..bd] : b;

        if (!System.Version.TryParse(aCore, out var av) || !System.Version.TryParse(bCore, out var bv))
            return string.CompareOrdinal(a, b);

        var cmp = av.CompareTo(bv);
        if (cmp != 0) return cmp;

        var aHasPre = ad >= 0;
        var bHasPre = bd >= 0;
        if (aHasPre && !bHasPre) return -1;
        if (!aHasPre && bHasPre) return 1;
        if (!aHasPre && !bHasPre) return 0;
        return string.CompareOrdinal(a[(ad + 1)..], b[(bd + 1)..]);
    }

    private static string GetCachePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir)) baseDir = Path.GetTempPath();
        return Path.Combine(baseDir, "nebra", "update-check.json");
    }

    private static UpdateCheckCache? ReadCache()
    {
        try
        {
            var path = GetCachePath();
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<UpdateCheckCache>(fs);
        }
        catch { return null; }
    }

    private static void WriteCache(string latestVersion)
    {
        try
        {
            var path = GetCachePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new UpdateCheckCache(DateTimeOffset.UtcNow, latestVersion));
            File.WriteAllText(path, json);
        }
        catch { /* best-effort */ }
    }

    private sealed record UpdateCheckCache(
        [property: JsonPropertyName("checkedAt")] DateTimeOffset CheckedAt,
        [property: JsonPropertyName("latestVersion")] string LatestVersion);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
