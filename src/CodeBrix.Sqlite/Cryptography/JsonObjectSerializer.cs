using System;
using System.Text.Json;

namespace CodeBrix.Sqlite.Cryptography;

/// <summary>
/// The default <see cref="IObjectSerializer"/> implementation, based on System.Text.Json.
/// Serializes public properties and fields, so common shapes like POCOs, records, tuples,
/// lists and dictionaries round-trip without additional configuration.
/// </summary>
public class JsonObjectSerializer : IObjectSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates an instance of the serializer with default options (fields included).
    /// </summary>
    public JsonObjectSerializer()
        : this(null)
    {
    }

    /// <summary>
    /// Creates an instance of the serializer with the specified System.Text.Json options.
    /// </summary>
    /// <param name="options">The serializer options to use; when null, default options with <c>IncludeFields = true</c> are used.</param>
    public JsonObjectSerializer(JsonSerializerOptions options)
    {
        _options = options ?? new JsonSerializerOptions { IncludeFields = true };
    }

    /// <inheritdoc />
    public string Serialize(object value)
    {
        if (value == null) { throw new ArgumentNullException(nameof(value)); }
        return JsonSerializer.Serialize(value, value.GetType(), _options);
    }

    /// <inheritdoc />
    public T Deserialize<T>(string serialized)
    {
        if (serialized == null) { throw new ArgumentNullException(nameof(serialized)); }
        return JsonSerializer.Deserialize<T>(serialized, _options);
    }
}
