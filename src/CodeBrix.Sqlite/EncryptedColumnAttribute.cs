using System;

namespace CodeBrix.Sqlite;

/// <summary>
/// Marks a property of a plain result type (POCO) as stored in an encrypted column: when the
/// Dapper-style <see cref="SqliteMapper"/> methods materialize a row, the column's string value is
/// decrypted with the crypt engine into the property's type instead of being assigned directly.
/// Not needed for <see cref="CodeBrix.Sqlite.EncryptedTables.EncryptedTableItem"/> types — those
/// are materialized from their whole-object Encrypted_Object column automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class EncryptedColumnAttribute : Attribute
{
}
