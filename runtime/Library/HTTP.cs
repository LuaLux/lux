using System.Text;
using System.Text.Json;
using Nebra.Runtime.Bindings;

namespace Nebra.Runtime.Library;

[NebraExport("http")]
public sealed class HTTP
{
    private static readonly HttpClient _sharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    [NebraExport("get")]
    public static NebraTable Get(string url, IDictionary<string, object?>? options = null)
        => Request("GET", url, options);

    [NebraExport("post")]
    public static NebraTable Post(string url, IDictionary<string, object?>? options = null)
        => Request("POST", url, options);

    [NebraExport("put")]
    public static NebraTable Put(string url, IDictionary<string, object?>? options = null)
        => Request("PUT", url, options);

    [NebraExport("patch")]
    public static NebraTable Patch(string url, IDictionary<string, object?>? options = null)
        => Request("PATCH", url, options);

    [NebraExport("delete")]
    public static NebraTable Delete(string url, IDictionary<string, object?>? options = null)
        => Request("DELETE", url, options);

    [NebraExport("postjson")]
    public static NebraTable PostJson(string url, object? payload)
    {
        var opts = new Dictionary<string, object?> { ["json"] = payload };
        return Request("POST", url, opts);
    }

    /// <summary>
    /// Performs an HTTP request. Supported option keys:
    /// <c>headers</c> (table), <c>query</c> (table), <c>body</c> (string),
    /// <c>json</c> (any — serialized as JSON with Content-Type application/json),
    /// <c>timeout</c> (seconds, number).
    /// Returns a table with <c>status</c>, <c>body</c>, <c>ok</c> and <c>headers</c>.
    /// </summary>
    [NebraExport("request")]
    public static NebraTable Request(string method, string url, IDictionary<string, object?>? options = null)
    {
        var req = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), BuildUri(url, options));

        if (options != null)
        {
            if (options.TryGetValue("headers", out var h) && h is IDictionary<string, object?> headers)
            {
                foreach (var kv in headers)
                {
                    var value = kv.Value?.ToString();
                    if (value == null) continue;
                    if (!req.Headers.TryAddWithoutValidation(kv.Key, value))
                    {
                        // content-type etc. may need to go on the content; stash and apply below
                    }
                }
            }

            if (options.TryGetValue("json", out var j))
            {
                var json = JsonSerializer.Serialize(ToJsonNode(j));
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (options.TryGetValue("body", out var b))
            {
                req.Content = new StringContent(b?.ToString() ?? "", Encoding.UTF8);
            }

            if (req.Content != null && options.TryGetValue("headers", out var h2)
                && h2 is IDictionary<string, object?> hdrs2)
            {
                foreach (var kv in hdrs2)
                {
                    if (!kv.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)) continue;
                    var ct = kv.Value?.ToString();
                    if (ct == null) continue;
                    req.Content.Headers.Remove("Content-Type");
                    req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
                }
            }
        }

        var timeout = default(TimeSpan?);
        if (options != null && options.TryGetValue("timeout", out var t) && t != null)
        {
            double seconds = t switch
            {
                long l => l,
                int i => i,
                double d => d,
                float f => f,
                string s when double.TryParse(s, out var ds) => ds,
                _ => 0
            };
            if (seconds > 0) timeout = TimeSpan.FromSeconds(seconds);
        }

        using var cts = new System.Threading.CancellationTokenSource(timeout ?? _sharedClient.Timeout);
        var response = _sharedClient.SendAsync(req, cts.Token).GetAwaiter().GetResult();
        return ParseResponseResult(response);
    }

    /// <summary>
    /// Downloads a file to the given path. Creates intermediate directories.
    /// Returns the number of bytes written.
    /// </summary>
    [NebraExport("download")]
    public static long Download(string url, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var response = _sharedClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var output = File.Create(path);
        input.CopyTo(output);
        return output.Length;
    }

    /// <summary>
    /// Downloads the response body as a string.
    /// </summary>
    [NebraExport("getString")]
    public static string GetString(string url)
        => _sharedClient.GetStringAsync(url).GetAwaiter().GetResult();

    private static Uri BuildUri(string url, IDictionary<string, object?>? options)
    {
        if (options == null || !options.TryGetValue("query", out var q) || q is not IDictionary<string, object?> query)
            return new Uri(url);

        var ub = new UriBuilder(url);
        var existing = ub.Query.TrimStart('?');
        var sb = new StringBuilder(existing);
        foreach (var kv in query)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value?.ToString() ?? ""));
        }
        ub.Query = sb.ToString();
        return ub.Uri;
    }

    private static NebraTable ParseResponseResult(HttpResponseMessage response)
    {
        var headers = new NebraTable();
        foreach (var header in response.Headers)
            headers.Set(header.Key, string.Join(", ", header.Value));
        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
                headers.Set(header.Key, string.Join(", ", header.Value));
        }

        var status = (int)response.StatusCode;
        var result = new NebraTable();
        result.Set("status", status);
        result.Set("ok", status >= 200 && status < 300);
        result.Set("body", response.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
        result.SetTable("headers", headers);
        return result;
    }

    /// <summary>
    /// Converts a CLR value tree (Dictionary, List, primitives) into a form
    /// <c>System.Text.Json</c> can serialize straightforwardly.
    /// </summary>
    private static object? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            IDictionary<string, object?> dict => dict.ToDictionary(kv => kv.Key, kv => ToJsonNode(kv.Value)),
            System.Collections.IList list => list.Cast<object?>().Select(ToJsonNode).ToList(),
            _ => value
        };
    }
}
