using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    /// <param name="options">An options template, copied without mutation. When null, defaults with
    /// <c>IncludeFields = true</c> are used. A supplied type-info resolver is honored; otherwise
    /// an explicit reflection resolver is installed.</param>
    /// <remarks>Full-trimmed apps must preserve reflected model members or supply source-generated
    /// metadata through <see cref="JsonSerializerOptions.TypeInfoResolver"/>. This serializer does not
    /// change the application-wide JSON reflection switch.</remarks>
    public JsonObjectSerializer(JsonSerializerOptions options)
    {
        // A private copy lets frozen or shared caller options remain untouched.
        _options = options == null
            ? new JsonSerializerOptions { IncludeFields = true }
            : new JsonSerializerOptions(options);
        _options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
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
