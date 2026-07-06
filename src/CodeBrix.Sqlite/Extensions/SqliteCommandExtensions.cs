using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Enumerations;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite.Extensions;

/// <summary>
/// Extension methods for <see cref="SqliteCommand"/> that add encrypted-parameter support,
/// encrypted scalar execution, and row-id-returning execution.
/// </summary>
public static class SqliteCommandExtensions
{
    /// <summary>
    /// Encrypts the specified value with the crypt engine and adds it to the command as a string
    /// parameter. A null value is added as a database NULL.
    /// </summary>
    /// <param name="command">The command to add the parameter to.</param>
    /// <param name="parameterName">The parameter name (with or without a leading '@').</param>
    /// <param name="value">The value to encrypt; may be any serializable CLR object.</param>
    /// <param name="cryptEngine">The crypt engine used to encrypt the value.</param>
    /// <returns>The created parameter.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/>, <paramref name="parameterName"/>, or <paramref name="cryptEngine"/> is null.</exception>
    public static SqliteParameter AddEncryptedParameter(this SqliteCommand command, string parameterName, object value, IObjectCryptEngine cryptEngine)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (parameterName == null) { throw new ArgumentNullException(nameof(parameterName)); }
        if (String.IsNullOrWhiteSpace(parameterName)) { throw new ArgumentException("The parameter name cannot be empty or whitespace.", nameof(parameterName)); }
        if (cryptEngine == null) { throw new ArgumentNullException(nameof(cryptEngine)); }
        string encrypted = cryptEngine.EncryptObject(value);
        return command.Parameters.AddWithValue(parameterName, (object)encrypted ?? DBNull.Value);
    }

    /// <summary>
    /// Executes the command and decrypts the first column of the first row of the result set into
    /// an object of the requested type. This is the encrypted counterpart of
    /// <see cref="SqliteCommand.ExecuteScalar"/>.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="dbNullHandling">How to behave when the result value is NULL or the result set is empty.</param>
    /// <returns>The decrypted object, or the type's default value for a NULL result when
    /// <see cref="DbNullHandling.ReturnTypeDefaultValue"/> is specified.</returns>
    /// <exception cref="DbNullValueException">Thrown when the result is NULL or empty and
    /// <see cref="DbNullHandling.ThrowDbNullException"/> is specified.</exception>
    public static T ExecuteDecrypt<T>(this SqliteCommand command, IObjectCryptEngine cryptEngine, DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (cryptEngine == null) { throw new ArgumentNullException(nameof(cryptEngine)); }
        object result = command.ExecuteScalar();
        return DecryptScalar<T>(result, cryptEngine, dbNullHandling);
    }

    /// <summary>
    /// Asynchronously executes the command and decrypts the first column of the first row of the
    /// result set into an object of the requested type.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="dbNullHandling">How to behave when the result value is NULL or the result set is empty.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the decrypted object.</returns>
    /// <exception cref="DbNullValueException">Thrown when the result is NULL or empty and
    /// <see cref="DbNullHandling.ThrowDbNullException"/> is specified.</exception>
    public static async Task<T> ExecuteDecryptAsync<T>(this SqliteCommand command, IObjectCryptEngine cryptEngine, DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException, CancellationToken cancellationToken = default)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (cryptEngine == null) { throw new ArgumentNullException(nameof(cryptEngine)); }
        object result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return DecryptScalar<T>(result, cryptEngine, dbNullHandling);
    }

    /// <summary>
    /// Executes the command (typically an INSERT) and returns the row id of the last inserted row
    /// on the command's connection — the value of <c>last_insert_rowid()</c>.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <returns>The row id of the last inserted row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the command has no connection.</exception>
    public static long ExecuteReturnRowId(this SqliteCommand command)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (command.Connection == null) { throw new ArgumentException("The command has no connection.", nameof(command)); }
        command.ExecuteNonQuery();
        using (SqliteCommand rowIdCommand = command.Connection.CreateCommand())
        {
            rowIdCommand.CommandText = "SELECT last_insert_rowid();";
            return (long)rowIdCommand.ExecuteScalar();
        }
    }

    /// <summary>
    /// Asynchronously executes the command (typically an INSERT) and returns the row id of the
    /// last inserted row on the command's connection.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the row id of the last inserted row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the command has no connection.</exception>
    public static async Task<long> ExecuteReturnRowIdAsync(this SqliteCommand command, CancellationToken cancellationToken = default)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (command.Connection == null) { throw new ArgumentException("The command has no connection.", nameof(command)); }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        using (SqliteCommand rowIdCommand = command.Connection.CreateCommand())
        {
            rowIdCommand.CommandText = "SELECT last_insert_rowid();";
            return (long)await rowIdCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static T DecryptScalar<T>(object result, IObjectCryptEngine cryptEngine, DbNullHandling dbNullHandling)
    {
        if (result == null || result == DBNull.Value)
        {
            if (dbNullHandling == DbNullHandling.ThrowDbNullException)
            {
                throw new DbNullValueException("The query produced a NULL (or empty) result that cannot be decrypted.");
            }
            return default;
        }
        return cryptEngine.DecryptObject<T>((string)result);
    }
}
