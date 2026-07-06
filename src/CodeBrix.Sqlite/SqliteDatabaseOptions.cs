using CodeBrix.Sqlite.Cryptography;

namespace CodeBrix.Sqlite;

/// <summary>
/// Options that control how a <see cref="SqliteDatabase"/> opens and configures its underlying
/// SQLite database connection.
/// </summary>
public class SqliteDatabaseOptions
{
    /// <summary>
    /// When true (the default), the database is switched to write-ahead-logging journal mode
    /// (<c>PRAGMA journal_mode=WAL</c>) when the connection is opened.
    /// </summary>
    public bool UseWriteAheadLogging { get; set; } = true;

    /// <summary>
    /// When true (the default), foreign-key constraint enforcement
    /// (<c>PRAGMA foreign_keys=ON</c>) is enabled when the connection is opened.
    /// </summary>
    public bool EnforceForeignKeys { get; set; } = true;

    /// <summary>
    /// When true (the default), the database file is created if it does not already exist.
    /// When false, opening a connection to a missing file fails.
    /// </summary>
    public bool CreateIfMissing { get; set; } = true;

    /// <summary>
    /// The object serializer exposed via <see cref="SqliteDatabase.Serializer"/> for consumer code.
    /// When null (the default), a <see cref="JsonObjectSerializer"/> is exposed. Note that crypt
    /// engines carry their OWN serializer for encrypt/decrypt operations (see the
    /// <see cref="AesGcmCryptEngine"/> constructors) — setting this option does not change how an
    /// already-constructed engine serializes.
    /// </summary>
    public IObjectSerializer Serializer { get; set; }
}
