using System.Diagnostics;

namespace Nebra.PackageManager;

/// <summary>
/// Thin wrapper around the system <c>git</c> binary. Runs processes async, captures stdout/stderr
/// and exit code. All network/auth flows are delegated to git itself (credential helper, SSH agent).
/// </summary>
public static class GitRunner
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string? workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new PackageManagerException("failed to start 'git' — is git installed and on PATH?");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var (ec, _, _) = await RunAsync(null, "--version");
            return ec == 0;
        }
        catch
        {
            return false;
        }
    }
}
