using System;

namespace CodeBrix.Sqlite.Exceptions;

/// <summary>
/// Thrown when an <see cref="CodeBrix.Sqlite.EncryptedTables.EncryptedTable{T}"/> operation fails —
/// for example, an invalid table name, an unsupported attribute combination on an item property,
/// or a requested item that does not exist.
/// </summary>
public class EncryptedTableException : CodeBrixSqliteException
{
    /// <summary>
    /// Creates an instance of the exception with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public EncryptedTableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance of the exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public EncryptedTableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
