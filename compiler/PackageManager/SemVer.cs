namespace Nebra.PackageManager;

/// <summary>
/// Minimal semver (major.minor.patch[-pre]). Tolerates leading 'v'.
/// </summary>
public sealed class SemVer : IComparable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? PreRelease { get; }
    public string Raw { get; }

    private SemVer(int major, int minor, int patch, string? pre, string raw)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = pre;
        Raw = raw;
    }

    public static bool TryParse(string input, out SemVer? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        string? pre = null;
        var preIdx = s.IndexOf('-');
        if (preIdx >= 0)
        {
            pre = s[(preIdx + 1)..];
            s = s[..preIdx];
        }

        var plusIdx = s.IndexOf('+');
        if (plusIdx >= 0) s = s[..plusIdx];

        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        if (!int.TryParse(parts[2], out var patch)) return false;

        version = new SemVer(major, minor, patch, pre, input.Trim());
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return string.Compare(PreRelease, other.PreRelease, StringComparison.Ordinal);
    }

    public override string ToString() => Raw;
}

public enum SemVerRangeKind { Exact, Caret, Tilde, Star }

/// <summary>
/// Range-matching for semver tags: <c>1.2.3</c>, <c>^1.2.3</c>, <c>~1.2.3</c>, <c>*</c>, <c>latest</c>.
/// </summary>
public sealed class SemVerRange
{
    public SemVerRangeKind Kind { get; init; }
    public SemVer? Base { get; init; }
    public string Raw { get; init; } = "";

    public static bool TryParse(string input, out SemVerRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();

        if (s == "*" || s.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            range = new SemVerRange { Kind = SemVerRangeKind.Star, Raw = input };
            return true;
        }

        if (s.StartsWith('^'))
        {
            if (!SemVer.TryParse(s[1..], out var v)) return false;
            range = new SemVerRange { Kind = SemVerRangeKind.Caret, Base = v, Raw = input };
            return true;
        }

        if (s.StartsWith('~'))
        {
            if (!SemVer.TryParse(s[1..], out var v)) return false;
            range = new SemVerRange { Kind = SemVerRangeKind.Tilde, Base = v, Raw = input };
            return true;
        }

        if (SemVer.TryParse(s, out var exact))
        {
            range = new SemVerRange { Kind = SemVerRangeKind.Exact, Base = exact, Raw = input };
            return true;
        }

        return false;
    }

    public bool Satisfies(SemVer v)
    {
        switch (Kind)
        {
            case SemVerRangeKind.Star:
                return v.PreRelease is null;
            case SemVerRangeKind.Exact:
                return Base!.CompareTo(v) == 0;
            case SemVerRangeKind.Caret:
                if (v.CompareTo(Base!) < 0) return false;
                if (Base!.Major == 0)
                {
                    if (Base.Minor == 0)
                        return v.Major == 0 && v.Minor == 0 && v.Patch == Base.Patch;
                    return v.Major == 0 && v.Minor == Base.Minor;
                }
                return v.Major == Base.Major;
            case SemVerRangeKind.Tilde:
                if (v.CompareTo(Base!) < 0) return false;
                return v.Major == Base!.Major && v.Minor == Base.Minor;
            default:
                return false;
        }
    }

    /// <summary>
    /// Picks the highest version from the candidate list that satisfies this range.
    /// Returns null when nothing matches.
    /// </summary>
    public SemVer? PickBest(IEnumerable<SemVer> candidates)
    {
        SemVer? best = null;
        foreach (var v in candidates)
        {
            if (v.PreRelease != null) continue;
            if (!Satisfies(v)) continue;
            if (best is null || v.CompareTo(best) > 0) best = v;
        }
        return best;
    }
}
