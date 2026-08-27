namespace Nebra.Configuration;

public sealed class ScriptsSection
{
    public List<string> PreBuild { get; set; } = [];

    public List<string> PostBuild { get; set; } = [];

    /// <summary>
    /// Lifecycle hook run after a package is fetched but before it is linked into
    /// <c>nebra_modules/</c>. Default off; opt-in via <c>nebra install --allow-scripts</c>.
    /// </summary>
    public List<string> PreInstall { get; set; } = [];

    /// <summary>
    /// Lifecycle hook run after a package has been linked into <c>nebra_modules/</c>.
    /// Default off; opt-in via <c>nebra install --allow-scripts</c>.
    /// </summary>
    public List<string> PostInstall { get; set; } = [];

    internal void Merge(ScriptsSection section)
    {
        if (section.PreBuild.Count > 0) PreBuild = section.PreBuild;
        if (section.PostBuild.Count > 0) PostBuild = section.PostBuild;
        if (section.PreInstall.Count > 0) PreInstall = section.PreInstall;
        if (section.PostInstall.Count > 0) PostInstall = section.PostInstall;
    }
}
