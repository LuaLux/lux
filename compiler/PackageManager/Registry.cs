using System.Net.Http;
using System.Text.Json;

namespace Nebra.PackageManager;

/// <summary>
/// Resolves bare alias specifiers (<c>nebra-http</c>) to git URLs via a remote JSON index
/// of shape <c>{ "name": "url", ... }</c>. The index is cached under
/// <c>$NEBRA_HOME/cache/registry/index.json</c> with a 24-hour TTL.
/// </summary>
public static class Registry
{
    public const string DefaultIndexUrl = "https://raw.githubusercontent.com/nebra-lang/pm-registry/main/index.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string IndexCachePath => Path.Combine(NebraHome.CacheRoot, "registry", "index.json");

    /// <summary>
    /// Returns the effective registry URL. Honors the <c>NEBRA_REGISTRY</c> environment variable
    /// when set, otherwise falls back to <see cref="DefaultIndexUrl"/>.
    /// </summary>
    public static string EffectiveUrl()
    {
        var env = Environment.GetEnvironmentVariable("NEBRA_REGISTRY");
        return string.IsNullOrWhiteSpace(env) ? DefaultIndexUrl : env;
    }

    /// <summary>
    /// Resolves an alias name to a git URL. Returns <c>null</c> if the alias is not in the index.
    /// </summary>
    public static async Task<string?> ResolveAsync(string aliasName, bool forceRefresh = false)
    {
        var index = await LoadIndexAsync(forceRefresh);
        return index.TryGetValue(aliasName, out var url) ? url : null;
    }

    /// <summary>
    /// Forces a refresh of the cached index from the network. Used by <c>nebra registry refresh</c>.
    /// </summary>
    public static async Task RefreshAsync()
    {
        await DownloadAndCacheAsync();
    }

    private static async Task<Dictionary<string, string>> LoadIndexAsync(bool forceRefresh)
    {
        if (!forceRefresh && TryReadFreshCache(out var cached))
            return cached;

        try
        {
            return await DownloadAndCacheAsync();
        }
        catch (Exception ex)
        {
            if (TryReadAnyCache(out var stale))
            {
                Console.Error.WriteLine($"warning: registry refresh failed ({ex.Message}); using stale cache");
                return stale;
            }
            throw new PackageManagerException($"could not load registry index from {EffectiveUrl()}: {ex.Message}");
        }
    }

    private static async Task<Dictionary<string, string>> DownloadAndCacheAsync()
    {
        var url = EffectiveUrl();
        var json = await Http.GetStringAsync(url);
        var parsed = Parse(json);
        Directory.CreateDirectory(Path.GetDirectoryName(IndexCachePath)!);
        await File.WriteAllTextAsync(IndexCachePath, json);
        return parsed;
    }

    private static bool TryReadFreshCache(out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(IndexCachePath)) return false;
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(IndexCachePath);
        if (age > CacheTtl) return false;
        return TryReadAnyCache(out result);
    }

    private static bool TryReadAnyCache(out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(IndexCachePath)) return false;
        try
        {
            result = Parse(File.ReadAllText(IndexCachePath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> Parse(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new PackageManagerException("registry index must be a JSON object of {name: url}");
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String) continue;
            var url = prop.Value.GetString();
            if (!string.IsNullOrWhiteSpace(url)) result[prop.Name] = url!;
        }
        return result;
    }
}
