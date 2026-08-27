using Nebra.PackageManager;

namespace Nebra.Configuration;

/// <summary>
/// Install-time knobs for the package manager.
/// </summary>
public sealed class InstallSection
{
    /// <summary>
    /// Controls how <c>nebra_modules/&lt;name&gt;</c> entries point into the global store.
    /// Default is <see cref="LinkerKind.Auto"/> which picks symlink on Unix and junction on Windows.
    /// </summary>
    public LinkerKind Linker { get; set; } = LinkerKind.Auto;

    /// <summary>
    /// Lifecycle scripts are off by default; install with <c>--allow-scripts</c> to opt in.
    /// Reserved for Phase 5.
    /// </summary>
    public bool AllowScripts { get; set; } = false;

    internal void Merge(InstallSection section)
    {
        Linker = Config.MergeVal(Linker, section.Linker, LinkerKind.Auto);
        AllowScripts = Config.MergeVal(AllowScripts, section.AllowScripts, false);
    }
}
