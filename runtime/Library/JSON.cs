using System.Text;
using System.Text.Json;
using Nebra.Runtime.Bindings;

namespace Nebra.Runtime.Library;

[NebraExport("json")]
public sealed class JSON
{
    /// <summary>
    /// Encodes a value tree to a JSON string. Pass <c>pretty = true</c> to
    /// get indented output.
    /// </summary>
    [NebraExport("encode")]
    public static string Encode(object? value, bool pretty = false)
    {
        var opts = new JsonSerializerOptions { WriteIndented = pretty };
        return JsonSerializer.Serialize(ToJsonValue(value), opts);
    }

    /// <summary>
    /// Decodes a JSON string into a value tree. Objects become tables with
    /// string keys; arrays become 1-indexed array-tables.
    /// </summary>
    [NebraExport("decode")]
    public static object? Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromJsonElement(doc.RootElement);
    }

    [NebraExport("pretty")]
    public static string Pretty(object? value) => Encode(value, pretty: true);

    private static object? ToJsonValue(object? value)
    {
        return value switch
        {
            null => null,
            IDictionary<string, object?> dict => dict.ToDictionary(kv => kv.Key, kv => ToJsonValue(kv.Value)),
            System.Collections.IList list => list.Cast<object?>().Select(ToJsonValue).ToList(),
            _ => value
        };
    }

    private static object? FromJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l;
                return element.GetDouble();
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(FromJsonElement(item));
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = FromJsonElement(prop.Value);
                return dict;
            default:
                return null;
        }
    }
}
