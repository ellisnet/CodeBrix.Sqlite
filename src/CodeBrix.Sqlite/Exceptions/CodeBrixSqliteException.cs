using System;

namespace CodeBrix.Sqlite.Exceptions;

/// <summary>
/// The base exception type for all exceptions thrown by the CodeBrix.Sqlite library.
/// </summary>
public class CodeBrixSqliteException : Exception
{
    /// <summary>
    /// Creates an instance of the exception with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CodeBrixSqliteException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance of the exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public CodeBrixSqliteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
