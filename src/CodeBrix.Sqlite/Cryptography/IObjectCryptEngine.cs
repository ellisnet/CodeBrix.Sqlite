using System;

namespace CodeBrix.Sqlite.Cryptography;

/// <summary>
/// Defines a pluggable cryptography 'engine' used by CodeBrix.Sqlite to encrypt CLR objects into
/// strings for storage in SQLite columns, and to decrypt those strings back into typed objects.
/// </summary>
public interface IObjectCryptEngine : IDisposable
{
    /// <summary>
    /// Serializes and encrypts the specified object, returning an opaque string suitable for
    /// storage in a SQLite TEXT column.
    /// </summary>
    /// <param name="valueToEncrypt">The object to encrypt; a null object produces a null result.</param>
    /// <returns>The encrypted value as a string, or null when <paramref name="valueToEncrypt"/> is null.</returns>
    string EncryptObject(object valueToEncrypt);

    /// <summary>
    /// Decrypts and deserializes the specified encrypted string back into an object of the
    /// requested type.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize the decrypted value into.</typeparam>
    /// <param name="encryptedValue">The encrypted string previously produced by <see cref="EncryptObject"/>.</param>
    /// <returns>The decrypted object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encryptedValue"/> is null.</exception>
    /// <exception cref="CodeBrix.Sqlite.Exceptions.ObjectCryptographyException">Thrown when decryption or deserialization fails.</exception>
    T DecryptObject<T>(string encryptedValue);
}
