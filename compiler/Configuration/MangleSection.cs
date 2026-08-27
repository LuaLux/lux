namespace Nebra.Configuration;

public sealed class MangleSection
{
    public bool Enabled { get; set; } = false;

    public bool MangleLocals { get; set; } = true;

    public bool MangleParams { get; set; } = true;

    public bool MangleTopLevel { get; set; } = false;

    public bool KeepFunctionNames { get; set; } = true;

    internal void Merge(MangleSection section)
    {
        Enabled = Config.MergeVal(Enabled, section.Enabled, false);
        MangleLocals = Config.MergeVal(MangleLocals, section.MangleLocals, true);
        MangleParams = Config.MergeVal(MangleParams, section.MangleParams, true);
        MangleTopLevel = Config.MergeVal(MangleTopLevel, section.MangleTopLevel, false);
        KeepFunctionNames = Config.MergeVal(KeepFunctionNames, section.KeepFunctionNames, true);
    }
}
