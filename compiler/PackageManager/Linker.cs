using System.Diagnostics;
using Nebra.Configuration.Converter;
using Tomlyn.Serialization;

namespace Nebra.PackageManager;

[TomlConverter(typeof(LinkerKindConverter))]
public enum LinkerKind
{
    Auto,
    Symlink,
    Junction,
    Copy,
}

/// <summary>
/// Creates a link (or falls back to a copy) from a project <c>nebra_modules/&lt;name&gt;</c> entry
/// into its store path. Strategy is platform-aware and degrades gracefully.
/// </summary>
public static class Linker
{
    public static LinkerKind Resolve(LinkerKind preferred)
    {
        if (preferred != LinkerKind.Auto) return preferred;
        return OperatingSystem.IsWindows() ? LinkerKind.Junction : LinkerKind.Symlink;
    }

    public static void Link(string source, string target, LinkerKind kind)
    {
        kind = Resolve(kind);
        DeleteExisting(target);

        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        switch (kind)
        {
            case LinkerKind.Symlink:
                TrySymlinkThenFallback(source, target);
                break;
            case LinkerKind.Junction:
                TryJunctionThenFallback(source, target);
                break;
            case LinkerKind.Copy:
                CopyDirectory(source, target);
                break;
            default:
                throw new NotSupportedException($"linker '{kind}' not supported");
        }
    }

    private static void TrySymlinkThenFallback(string source, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(target, source);
            return;
        }
        catch
        {
            if (OperatingSystem.IsWindows())
            {
                TryJunctionThenFallback(source, target);
                return;
            }
        }
        CopyDirectory(source, target);
    }

    private static void TryJunctionThenFallback(string source, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            TrySymlinkThenFallback(source, target);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add(source);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                CopyDirectory(source, target);
                return;
            }
            proc.WaitForExit();
            if (proc.ExitCode == 0) return;
        }
        catch
        {
            // fall through
        }
        CopyDirectory(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(target, rel));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void DeleteExisting(string target)
    {
        try
        {
            if (Directory.Exists(target))
            {
                var info = new DirectoryInfo(target);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    info.Delete();
                else
                    info.Delete(recursive: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch
        {
            // best-effort; if we can't delete, the subsequent create will fail and surface the real error
        }
    }
}
