using System;

namespace CodeBrix.Sqlite.Exceptions;

/// <summary>
/// Thrown when a database operation conflicts with the database's maintenance-mode state — either
/// a normal operation was attempted while the database is in maintenance mode, or a
/// maintenance-only operation was attempted while the database is not in maintenance mode.
/// </summary>
public class DatabaseMaintenanceException : CodeBrixSqliteException
{
    /// <summary>
    /// Creates an instance of the exception with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public DatabaseMaintenanceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance of the exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public DatabaseMaintenanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
