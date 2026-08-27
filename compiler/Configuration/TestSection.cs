namespace Nebra.Configuration;

/// <summary>
/// The [test] section of <see cref="Config"/>. Tunes the <c>nebra test</c> discovery
/// pipeline: which directories are scanned for test files and which filename
/// patterns count as tests.
/// </summary>
public sealed class TestSection
{
    /// <summary>
    /// Extra directories (relative to the project root) that are scanned recursively
    /// for test files. The project source directory is always included.
    /// </summary>
    public List<string> Dirs { get; set; } = ["tests", "test"];

    /// <summary>
    /// Glob-style filename suffixes that mark a file as a test. Anything inside one
    /// of the <see cref="Dirs"/> is always picked up; anywhere else, the filename
    /// must end with one of these suffixes (case-insensitive).
    /// </summary>
    public List<string> Patterns { get; set; } = ["_test.neb", ".test.neb"];

    /// <summary>
    /// Suppresses the per-test tick output, leaving only the summary on stdout.
    /// Useful in CI logs.
    /// </summary>
    public bool Quiet { get; set; } = false;

    internal void Merge(TestSection section)
    {
        Dirs.AddRange(section.Dirs);
        Patterns.AddRange(section.Patterns);
        Quiet = Config.MergeVal(Quiet, section.Quiet, false);
    }
}
