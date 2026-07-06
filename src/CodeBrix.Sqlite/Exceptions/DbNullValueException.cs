using System;

namespace CodeBrix.Sqlite.Exceptions;

/// <summary>
/// Thrown when a decryption operation encounters a NULL database column value and the caller
/// requested <see cref="CodeBrix.Sqlite.Enumerations.DbNullHandling.ThrowDbNullException"/> behavior.
/// </summary>
public class DbNullValueException : CodeBrixSqliteException
{
    /// <summary>
    /// Creates an instance of the exception with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public DbNullValueException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance of the exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public DbNullValueException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
