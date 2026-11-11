using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApolloNCC.Api;

/// <summary>
/// One Firestore document as plain values: string, double, bool, null, List and Dictionary.
/// The web app keeps loosely shaped rows, so fields are read by name and missing ones are simply absent.
/// </summary>
public sealed class Doc
{
    public Doc(string id, Dictionary<string, object?> fields)
    {
        this.Id = id;
        this.Fields = fields;
    }

    public string Id { get; }

    public Dictionary<string, object?> Fields { get; }

    public string Str(string key) => this.Fields.GetValueOrDefault(key) as string ?? "";

    public double? Num(string key) => this.Fields.GetValueOrDefault(key) is double d && !double.IsNaN(d) ? d : null;

    public bool Bool(string key) => this.Fields.GetValueOrDefault(key) is true;

    public List<object?> List(string key) => this.Fields.GetValueOrDefault(key) as List<object?> ?? [];

    public Dictionary<string, object?> Map(string key) =>
        this.Fields.GetValueOrDefault(key) as Dictionary<string, object?> ?? [];

    /// <summary>Reads Firestore's REST form: {"name": ".../coll/id", "fields": {"a": {"stringValue": "x"}}}.</summary>
    public static Doc FromRest(JsonElement document)
    {
        var name = document.GetProperty("name").GetString() ?? "";
        var fields = document.TryGetProperty("fields", out var f) ? DecodeMap(f) : [];
        return new Doc(name[(name.LastIndexOf('/') + 1)..], fields);
    }

    public static JsonObject ToRestFields(IReadOnlyDictionary<string, object?> fields)
    {
        var o = new JsonObject();
        foreach (var (k, v) in fields)
            o[k] = Encode(v);
        return o;
    }

    private static Dictionary<string, object?> DecodeMap(JsonElement fields)
    {
        var map = new Dictionary<string, object?>();
        foreach (var p in fields.EnumerateObject())
            map[p.Name] = Decode(p.Value);
        return map;
    }

    private static object? Decode(JsonElement v)
    {
        foreach (var p in v.EnumerateObject())
        {
            switch (p.Name)
            {
                case "stringValue":
                case "referenceValue":
                case "timestampValue":
                    return p.Value.GetString();
                case "booleanValue":
                    return p.Value.GetBoolean();
                case "integerValue":
                    // Firestore sends 64-bit integers as strings.
                    return double.Parse(p.Value.GetString()!, CultureInfo.InvariantCulture);
                case "doubleValue":
                    return p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : double.NaN;
                case "arrayValue":
                    var list = new List<object?>();
                    if (p.Value.TryGetProperty("values", out var values))
                    {
                        foreach (var item in values.EnumerateArray())
                            list.Add(Decode(item));
                    }

                    return list;
                case "mapValue":
                    return p.Value.TryGetProperty("fields", out var f) ? DecodeMap(f) : new Dictionary<string, object?>();
                default:
                    return null;
            }
        }

        return null;
    }

    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["nullValue"] = null },
        string s => new JsonObject { ["stringValue"] = s },
        bool b => new JsonObject { ["booleanValue"] = b },
        int i => new JsonObject { ["integerValue"] = i.ToString(CultureInfo.InvariantCulture) },
        long l => new JsonObject { ["integerValue"] = l.ToString(CultureInfo.InvariantCulture) },

        // The site writes JavaScript numbers, which Firestore stores as integers when they are whole.
        double d when Math.Abs(d) < 9e15 && d == Math.Floor(d) =>
            new JsonObject { ["integerValue"] = ((long)d).ToString(CultureInfo.InvariantCulture) },
        double d => new JsonObject { ["doubleValue"] = d },
        IReadOnlyDictionary<string, object?> m => new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = ToRestFields(m) } },
        IEnumerable<object?> list => new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = EncodeList(list) } },
        _ => throw new ArgumentException($"Cannot store a {value.GetType().Name} in Firestore."),
    };

    private static JsonArray EncodeList(IEnumerable<object?> list)
    {
        var a = new JsonArray();
        foreach (var item in list)
            a.Add(Encode(item));
        return a;
    }
}
