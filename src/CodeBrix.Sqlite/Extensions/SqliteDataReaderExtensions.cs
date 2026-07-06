using System;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Enumerations;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite.Extensions;

/// <summary>
/// Extension methods for <see cref="SqliteDataReader"/> that read and decrypt encrypted column
/// values into typed objects.
/// </summary>
public static class SqliteDataReaderExtensions
{
    /// <summary>
    /// Reads the encrypted value at the specified column ordinal and decrypts it into an object of
    /// the requested type.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="reader">The data reader positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="dbNullHandling">How to behave when the column value is NULL.</param>
    /// <returns>The decrypted object, or the type's default value for a NULL column when
    /// <see cref="DbNullHandling.ReturnTypeDefaultValue"/> is specified.</returns>
    /// <exception cref="DbNullValueException">Thrown when the column value is NULL and
    /// <see cref="DbNullHandling.ThrowDbNullException"/> is specified.</exception>
    public static T GetDecrypted<T>(this SqliteDataReader reader, int ordinal, IObjectCryptEngine cryptEngine, DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    {
        if (reader == null) { throw new ArgumentNullException(nameof(reader)); }
        if (cryptEngine == null) { throw new ArgumentNullException(nameof(cryptEngine)); }
        if (reader.IsDBNull(ordinal))
        {
            if (dbNullHandling == DbNullHandling.ThrowDbNullException)
            {
                throw new DbNullValueException($"The value of column {ordinal} is NULL and cannot be decrypted.");
            }
            return default;
        }
        return cryptEngine.DecryptObject<T>(reader.GetString(ordinal));
    }

    /// <summary>
    /// Reads the encrypted value in the specified column and decrypts it into an object of the
    /// requested type.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="reader">The data reader positioned on a row.</param>
    /// <param name="columnName">The name of the column to read.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="dbNullHandling">How to behave when the column value is NULL.</param>
    /// <returns>The decrypted object, or the type's default value for a NULL column when
    /// <see cref="DbNullHandling.ReturnTypeDefaultValue"/> is specified.</returns>
    /// <exception cref="DbNullValueException">Thrown when the column value is NULL and
    /// <see cref="DbNullHandling.ThrowDbNullException"/> is specified.</exception>
    public static T GetDecrypted<T>(this SqliteDataReader reader, string columnName, IObjectCryptEngine cryptEngine, DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    {
        if (reader == null) { throw new ArgumentNullException(nameof(reader)); }
        if (columnName == null) { throw new ArgumentNullException(nameof(columnName)); }
        return reader.GetDecrypted<T>(reader.GetOrdinal(columnName), cryptEngine, dbNullHandling);
    }

    /// <summary>
    /// Attempts to read and decrypt the encrypted value at the specified column ordinal.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="reader">The data reader positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="value">When the method returns true, the decrypted object; otherwise the type's default value.</param>
    /// <returns>True when the value was decrypted successfully; false when the column value was
    /// NULL or decryption failed.</returns>
    public static bool TryDecrypt<T>(this SqliteDataReader reader, int ordinal, IObjectCryptEngine cryptEngine, out T value)
    {
        if (reader == null) { throw new ArgumentNullException(nameof(reader)); }
        if (cryptEngine == null) { throw new ArgumentNullException(nameof(cryptEngine)); }
        value = default;
        if (reader.IsDBNull(ordinal)) { return false; }
        try
        {
            value = cryptEngine.DecryptObject<T>(reader.GetString(ordinal));
            return true;
        }
        catch (ObjectCryptographyException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to read and decrypt the encrypted value in the specified column.
    /// </summary>
    /// <typeparam name="T">The type of object to decrypt the value into.</typeparam>
    /// <param name="reader">The data reader positioned on a row.</param>
    /// <param name="columnName">The name of the column to read.</param>
    /// <param name="cryptEngine">The crypt engine used to decrypt the value.</param>
    /// <param name="value">When the method returns true, the decrypted object; otherwise the type's default value.</param>
    /// <returns>True when the value was decrypted successfully; false when the column value was
    /// NULL or decryption failed.</returns>
    public static bool TryDecrypt<T>(this SqliteDataReader reader, string columnName, IObjectCryptEngine cryptEngine, out T value)
    {
        if (reader == null) { throw new ArgumentNullException(nameof(reader)); }
        if (columnName == null) { throw new ArgumentNullException(nameof(columnName)); }
        return reader.TryDecrypt(reader.GetOrdinal(columnName), cryptEngine, out value);
    }
}
