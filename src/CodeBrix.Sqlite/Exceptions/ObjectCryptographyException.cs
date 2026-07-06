using System;

namespace CodeBrix.Sqlite.Exceptions;

/// <summary>
/// Thrown when an object encryption, decryption, or serialization operation fails — for example,
/// when a value cannot be decrypted because the key is wrong or the stored data is corrupt.
/// </summary>
public class ObjectCryptographyException : CodeBrixSqliteException
{
    /// <summary>
    /// Creates an instance of the exception with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ObjectCryptographyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance of the exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ObjectCryptographyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
