namespace CodeBrix.Sqlite.Cryptography;

/// <summary>
/// Defines a pluggable object serializer used to convert CLR objects to and from strings before
/// encryption and after decryption. The default implementation is <see cref="JsonObjectSerializer"/>.
/// </summary>
public interface IObjectSerializer
{
    /// <summary>
    /// Serializes the specified object to a string.
    /// </summary>
    /// <param name="value">The object to serialize.</param>
    /// <returns>The serialized representation of the object.</returns>
    string Serialize(object value);

    /// <summary>
    /// Deserializes the specified string back into an object of the requested type.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize into.</typeparam>
    /// <param name="serialized">The serialized representation previously produced by <see cref="Serialize"/>.</param>
    /// <returns>The deserialized object.</returns>
    T Deserialize<T>(string serialized);
}
