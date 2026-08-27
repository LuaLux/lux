using Nebra.Runtime.Bindings;

namespace Nebra.Runtime.Library;

[NebraExport("fs")]
public sealed class FS
{
    [NebraExport("readFile")]
    public static string ReadFile(string path) => File.ReadAllText(path);

    [NebraExport("writeFile")]
    public static void WriteFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    [NebraExport("appendFile")]
    public static void AppendFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.AppendAllText(path, content);
    }

    [NebraExport("exists")]
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    [NebraExport("isFile")]
    public static bool IsFile(string path) => File.Exists(path);

    [NebraExport("isDir")]
    public static bool IsDir(string path) => Directory.Exists(path);

    [NebraExport("mkdir")]
    public static void Mkdir(string path) => Directory.CreateDirectory(path);

    [NebraExport("remove")]
    public static void Remove(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    [NebraExport("copy")]
    public static void Copy(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
            return;
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(source, destination, overwrite: true);
    }

    [NebraExport("move")]
    public static void Move(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination, overwrite: true);
    }

    [NebraExport("listFiles")]
    public static List<object?> ListFiles(string path, string pattern = "*")
    {
        if (!Directory.Exists(path)) return [];
        return Directory.EnumerateFiles(path, pattern).Select(p => (object?)p).ToList();
    }

    [NebraExport("listDirs")]
    public static List<object?> ListDirs(string path)
    {
        if (!Directory.Exists(path)) return [];
        return Directory.EnumerateDirectories(path).Select(p => (object?)p).ToList();
    }

    [NebraExport("cwd")]
    public static string Cwd() => Environment.CurrentDirectory;

    [NebraExport("join")]
    public static string Join(string a, string b) => Path.Combine(a, b);

    [NebraExport("basename")]
    public static string Basename(string path) => Path.GetFileName(path);

    [NebraExport("dirname")]
    public static string Dirname(string path) => Path.GetDirectoryName(path) ?? "";

    [NebraExport("extname")]
    public static string Extname(string path) => Path.GetExtension(path);

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
            var target = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, target);
        }
    }
}
