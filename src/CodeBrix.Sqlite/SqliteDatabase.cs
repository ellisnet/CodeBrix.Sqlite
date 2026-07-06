using System;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite;

/// <summary>
/// The main entry-point class of CodeBrix.Sqlite: wraps a <see cref="SqliteConnection"/> to a
/// SQLite database file and layers on modern defaults (WAL journaling, enforced foreign keys),
/// an optional object crypt engine for encrypted column values, database maintenance mode,
/// safe quiesce-and-backup orchestration, and <c>user_version</c> schema-version helpers.
/// </summary>
public class SqliteDatabase : IDisposable
{
    //Lets the Dapper-style SqliteMapper extension methods on a bare SqliteConnection discover
    //  the SqliteDatabase (and so the crypt engine + maintenance-mode state) that owns it.
    private static readonly ConditionalWeakTable<SqliteConnection, SqliteDatabase> AmbientDatabases =
        new ConditionalWeakTable<SqliteConnection, SqliteDatabase>();

    private readonly string _databaseFilePath;
    private readonly IObjectCryptEngine _cryptEngine;
    private readonly IObjectSerializer _serializer;
    private readonly SqliteDatabaseOptions _options;
    private readonly SqliteConnection _connection;
    private readonly object _maintenanceLock = new object();
    private bool _isInMaintenanceMode;
    private bool _pragmasApplied;
    private bool _disposed;

    /// <summary>
    /// The full path of the SQLite database file.
    /// </summary>
    public string DatabaseFilePath => _databaseFilePath;

    /// <summary>
    /// The underlying Microsoft.Data.Sqlite connection. Operations executed directly against this
    /// connection bypass the maintenance-mode gate; prefer the methods on this class.
    /// </summary>
    public SqliteConnection Connection => _connection;

    /// <summary>
    /// The object crypt engine associated with this database, or null when none was provided.
    /// </summary>
    public IObjectCryptEngine CryptEngine => _cryptEngine;

    /// <summary>
    /// The object serializer used by encrypted-table operations against this database.
    /// </summary>
    public IObjectSerializer Serializer => _serializer;

    /// <summary>
    /// The options this database was created with.
    /// </summary>
    public SqliteDatabaseOptions Options => _options;

    /// <summary>
    /// The current state of the underlying connection.
    /// </summary>
    public ConnectionState State => _connection.State;

    /// <summary>
    /// True while the database is in maintenance mode; normal operations are blocked and throw
    /// a <see cref="DatabaseMaintenanceException"/> until <see cref="EndMaintenanceMode"/> is called.
    /// </summary>
    public bool IsInMaintenanceMode => _isInMaintenanceMode;

    /// <summary>
    /// Creates an instance of the database wrapper for the specified SQLite database file.
    /// The connection is not opened until <see cref="Open"/>, <see cref="SafeOpen"/>, or the
    /// first operation that needs it.
    /// </summary>
    /// <param name="databaseFilePath">The full path of the SQLite database file.</param>
    /// <param name="cryptEngine">Optional crypt engine for encrypted column values and encrypted tables.
    /// The database does not take ownership: disposing the database does not dispose the engine.</param>
    /// <param name="options">Optional database options; when null, defaults are used (WAL on,
    /// foreign keys on, create-if-missing on).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="databaseFilePath"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="databaseFilePath"/> is empty or whitespace.</exception>
    public SqliteDatabase(string databaseFilePath, IObjectCryptEngine cryptEngine = null, SqliteDatabaseOptions options = null)
    {
        if (databaseFilePath == null) { throw new ArgumentNullException(nameof(databaseFilePath)); }
        if (String.IsNullOrWhiteSpace(databaseFilePath)) { throw new ArgumentException("The database file path cannot be empty or whitespace.", nameof(databaseFilePath)); }
        _databaseFilePath = databaseFilePath;
        _cryptEngine = cryptEngine;
        _options = options ?? new SqliteDatabaseOptions();
        _serializer = _options.Serializer ?? new JsonObjectSerializer();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFilePath,
            Mode = _options.CreateIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite
        };
        _connection = new SqliteConnection(builder.ConnectionString);
        AmbientDatabases.Add(_connection, this);
    }

    internal static bool TryGetAmbientDatabase(SqliteConnection connection, out SqliteDatabase database)
        => AmbientDatabases.TryGetValue(connection, out database);

    #region Open / close

    /// <summary>
    /// Opens the connection and applies the configured connection pragmas (WAL journal mode,
    /// foreign-key enforcement). Throws if the connection is already open; see <see cref="SafeOpen"/>
    /// for an idempotent alternative.
    /// </summary>
    public void Open()
    {
        ThrowIfDisposed();
        _connection.Open();
        ApplyPragmas();
    }

    /// <summary>
    /// Asynchronously opens the connection and applies the configured connection pragmas.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the connection is open.</returns>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApplyPragmasAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the connection if it is not already open; does nothing when it is. This is the
    /// same behavior as the <c>SafeOpen()</c> method of the classic SimpleAdo.Sqlite library.
    /// </summary>
    public void SafeOpen()
    {
        ThrowIfDisposed();
        if (_connection.State != ConnectionState.Open) { Open(); }
    }

    /// <summary>
    /// Asynchronously opens the connection if it is not already open.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the connection is open.</returns>
    public async Task SafeOpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_connection.State != ConnectionState.Open)
        {
            await OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the underlying connection if it is open.
    /// </summary>
    public void Close()
    {
        ThrowIfDisposed();
        if (_connection.State != ConnectionState.Closed) { _connection.Close(); }
    }

    private void ApplyPragmas()
    {
        if (_pragmasApplied) { return; }
        using (SqliteCommand command = _connection.CreateCommand())
        {
            if (_options.UseWriteAheadLogging)
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteScalar();
            }
            if (_options.EnforceForeignKeys)
            {
                command.CommandText = "PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }
        }
        _pragmasApplied = true;
    }

    private async Task ApplyPragmasAsync(CancellationToken cancellationToken)
    {
        if (_pragmasApplied) { return; }
        using (SqliteCommand command = _connection.CreateCommand())
        {
            if (_options.UseWriteAheadLogging)
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_options.EnforceForeignKeys)
            {
                command.CommandText = "PRAGMA foreign_keys=ON;";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        _pragmasApplied = true;
    }

    #endregion

    #region Command creation and execution

    /// <summary>
    /// Creates a command against this database, honoring the maintenance-mode gate at creation
    /// time: while the database is in maintenance mode only commands created with
    /// <paramref name="forMaintenance"/> set to true are permitted, and vice versa.
    /// </summary>
    /// <param name="commandText">Optional SQL text to assign to the command.</param>
    /// <param name="forMaintenance">True when the command is part of a maintenance operation.</param>
    /// <returns>The created command.</returns>
    /// <exception cref="DatabaseMaintenanceException">Thrown when the requested command kind conflicts
    /// with the database's current maintenance-mode state.</exception>
    public SqliteCommand CreateCommand(string commandText = null, bool forMaintenance = false)
    {
        ThrowIfDisposed();
        EnsureOperationAllowed(forMaintenance);
        SqliteCommand command = _connection.CreateCommand();
        if (commandText != null) { command.CommandText = commandText; }
        return command;
    }

    /// <summary>
    /// Opens the connection if necessary and executes the specified SQL statement, returning the
    /// number of rows affected.
    /// </summary>
    /// <param name="sql">The SQL statement to execute.</param>
    /// <param name="forMaintenance">True when the statement is part of a maintenance operation.</param>
    /// <returns>The number of rows affected.</returns>
    public int ExecuteNonQuery(string sql, bool forMaintenance = false)
    {
        ValidateSql(sql);
        SafeOpen();
        EnsureOperationAllowed(forMaintenance);
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = sql;
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Asynchronously opens the connection if necessary and executes the specified SQL statement,
    /// returning the number of rows affected.
    /// </summary>
    /// <param name="sql">The SQL statement to execute.</param>
    /// <param name="forMaintenance">True when the statement is part of a maintenance operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the number of rows affected.</returns>
    public async Task<int> ExecuteNonQueryAsync(string sql, bool forMaintenance = false, CancellationToken cancellationToken = default)
    {
        ValidateSql(sql);
        await SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        EnsureOperationAllowed(forMaintenance);
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the connection if necessary and executes the specified SQL statement, returning the
    /// first column of the first row of the result set.
    /// </summary>
    /// <param name="sql">The SQL statement to execute.</param>
    /// <param name="forMaintenance">True when the statement is part of a maintenance operation.</param>
    /// <returns>The scalar result, or null when the result set is empty.</returns>
    public object ExecuteScalar(string sql, bool forMaintenance = false)
    {
        ValidateSql(sql);
        SafeOpen();
        EnsureOperationAllowed(forMaintenance);
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
    }

    /// <summary>
    /// Asynchronously opens the connection if necessary and executes the specified SQL statement,
    /// returning the first column of the first row of the result set.
    /// </summary>
    /// <param name="sql">The SQL statement to execute.</param>
    /// <param name="forMaintenance">True when the statement is part of a maintenance operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the scalar result, or null when the result set is empty.</returns>
    public async Task<object> ExecuteScalarAsync(string sql, bool forMaintenance = false, CancellationToken cancellationToken = default)
    {
        ValidateSql(sql);
        await SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        EnsureOperationAllowed(forMaintenance);
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = sql;
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateSql(string sql)
    {
        if (sql == null) { throw new ArgumentNullException(nameof(sql)); }
        if (String.IsNullOrWhiteSpace(sql)) { throw new ArgumentException("The SQL statement cannot be empty or whitespace.", nameof(sql)); }
    }

    private void EnsureOperationAllowed(bool forMaintenance)
    {
        if (_isInMaintenanceMode && !forMaintenance)
        {
            throw new DatabaseMaintenanceException(
                "The database is in maintenance mode - normal operations are blocked until EndMaintenanceMode() is called.");
        }
        if (!_isInMaintenanceMode && forMaintenance)
        {
            throw new DatabaseMaintenanceException(
                "Maintenance operations require the database to be in maintenance mode - call BeginMaintenanceMode() first.");
        }
    }

    #endregion

    #region Maintenance mode

    /// <summary>
    /// Places the database in maintenance mode. While in maintenance mode, normal operations throw
    /// a <see cref="DatabaseMaintenanceException"/>; only operations flagged as maintenance
    /// operations are permitted. Typically used around backups and schema-modifying work.
    /// </summary>
    /// <returns>True when the database is in maintenance mode after the call (including when it already was).</returns>
    public bool BeginMaintenanceMode()
    {
        ThrowIfDisposed();
        lock (_maintenanceLock)
        {
            _isInMaintenanceMode = true;
            return _isInMaintenanceMode;
        }
    }

    /// <summary>
    /// Ends maintenance mode and returns the database to normal operations.
    /// </summary>
    /// <returns>True when the database is out of maintenance mode after the call (including when it already was).</returns>
    public bool EndMaintenanceMode()
    {
        ThrowIfDisposed();
        lock (_maintenanceLock)
        {
            _isInMaintenanceMode = false;
            return !_isInMaintenanceMode;
        }
    }

    #endregion

    #region Schema version

    /// <summary>
    /// Reads the database's <c>user_version</c> value — typically used to track the version of the
    /// database schema (DDL). Leaves the connection in the open/closed state it was found in.
    /// </summary>
    /// <returns>The current schema version.</returns>
    public long GetSchemaVersion()
    {
        ThrowIfDisposed();
        bool wasClosed = _connection.State != ConnectionState.Open;
        SafeOpen();
        try
        {
            using (SqliteCommand command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                return (long)command.ExecuteScalar();
            }
        }
        finally
        {
            if (wasClosed) { Close(); }
        }
    }

    /// <summary>
    /// Asynchronously reads the database's <c>user_version</c> value.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the current schema version.</returns>
    public async Task<long> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        bool wasClosed = _connection.State != ConnectionState.Open;
        await SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using (SqliteCommand command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                return (long)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (wasClosed) { Close(); }
        }
    }

    /// <summary>
    /// Sets the database's <c>user_version</c> value. The write happens inside maintenance mode
    /// (entered and exited automatically when the database was not already in maintenance mode),
    /// and the connection is left in the open/closed state it was found in.
    /// </summary>
    /// <param name="version">The schema version to record.</param>
    public void SetSchemaVersion(long version)
    {
        ThrowIfDisposed();
        bool wasClosed = _connection.State != ConnectionState.Open;
        SafeOpen();
        bool wasInMaintenanceMode = _isInMaintenanceMode;
        if (!wasInMaintenanceMode) { BeginMaintenanceMode(); }
        try
        {
            ExecuteNonQuery($"PRAGMA user_version = {version};", forMaintenance: true);
        }
        finally
        {
            if (!wasInMaintenanceMode) { EndMaintenanceMode(); }
            if (wasClosed) { Close(); }
        }
    }

    /// <summary>
    /// Asynchronously sets the database's <c>user_version</c> value inside maintenance mode.
    /// </summary>
    /// <param name="version">The schema version to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the version has been written.</returns>
    public async Task SetSchemaVersionAsync(long version, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        bool wasClosed = _connection.State != ConnectionState.Open;
        await SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        bool wasInMaintenanceMode = _isInMaintenanceMode;
        if (!wasInMaintenanceMode) { BeginMaintenanceMode(); }
        try
        {
            await ExecuteNonQueryAsync($"PRAGMA user_version = {version};", forMaintenance: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!wasInMaintenanceMode) { EndMaintenanceMode(); }
            if (wasClosed) { Close(); }
        }
    }

    #endregion

    #region Backup and snapshot

    /// <summary>
    /// Safely backs up the database to the specified file using the orchestrated sequence:
    /// quiesce (maintenance mode) → WAL checkpoint (<c>PRAGMA wal_checkpoint(TRUNCATE)</c>) →
    /// SQLite online backup → resume. An existing file at the destination is overwritten.
    /// </summary>
    /// <param name="destinationFilePath">The full path of the backup file to create.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destinationFilePath"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destinationFilePath"/> is empty or whitespace.</exception>
    public void BackupToFile(string destinationFilePath)
    {
        ValidateDestinationPath(destinationFilePath);
        ThrowIfDisposed();
        SafeOpen();
        bool wasInMaintenanceMode = _isInMaintenanceMode;
        if (!wasInMaintenanceMode) { BeginMaintenanceMode(); }
        try
        {
            CheckpointWriteAheadLog();
            if (File.Exists(destinationFilePath)) { File.Delete(destinationFilePath); }
            CopyDatabaseTo(destinationFilePath);
        }
        finally
        {
            if (!wasInMaintenanceMode) { EndMaintenanceMode(); }
        }
    }

    /// <summary>
    /// Asynchronously backs up the database to the specified file using the same orchestration as
    /// <see cref="BackupToFile"/>. The checkpoint runs asynchronously; the SQLite online-backup page
    /// copy itself is a synchronous native operation.
    /// </summary>
    /// <param name="destinationFilePath">The full path of the backup file to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the backup file has been written.</returns>
    public async Task BackupToFileAsync(string destinationFilePath, CancellationToken cancellationToken = default)
    {
        ValidateDestinationPath(destinationFilePath);
        ThrowIfDisposed();
        await SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        bool wasInMaintenanceMode = _isInMaintenanceMode;
        if (!wasInMaintenanceMode) { BeginMaintenanceMode(); }
        try
        {
            await CheckpointWriteAheadLogAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(destinationFilePath)) { File.Delete(destinationFilePath); }
            CopyDatabaseTo(destinationFilePath);
        }
        finally
        {
            if (!wasInMaintenanceMode) { EndMaintenanceMode(); }
        }
    }

    /// <summary>
    /// Creates a consistent single-statement snapshot of the database at the specified file using
    /// SQLite's <c>VACUUM INTO</c>. Unlike <see cref="BackupToFile"/>, this does not require
    /// maintenance mode — SQLite guarantees a consistent snapshot — but the destination file must
    /// not already exist.
    /// </summary>
    /// <param name="destinationFilePath">The full path of the snapshot file to create.</param>
    /// <exception cref="IOException">Thrown when a file already exists at <paramref name="destinationFilePath"/>.</exception>
    public void SnapshotToFile(string destinationFilePath)
    {
        ValidateDestinationPath(destinationFilePath);
        ThrowIfDisposed();
        if (File.Exists(destinationFilePath))
        {
            throw new IOException($"A file already exists at the snapshot destination: {destinationFilePath}");
        }
        ExecuteNonQuery($"VACUUM INTO '{destinationFilePath.Replace("'", "''")}';");
    }

    /// <summary>
    /// Asynchronously creates a consistent single-statement snapshot of the database at the
    /// specified file using SQLite's <c>VACUUM INTO</c>.
    /// </summary>
    /// <param name="destinationFilePath">The full path of the snapshot file to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the snapshot file has been written.</returns>
    /// <exception cref="IOException">Thrown when a file already exists at <paramref name="destinationFilePath"/>.</exception>
    public Task SnapshotToFileAsync(string destinationFilePath, CancellationToken cancellationToken = default)
    {
        ValidateDestinationPath(destinationFilePath);
        ThrowIfDisposed();
        if (File.Exists(destinationFilePath))
        {
            throw new IOException($"A file already exists at the snapshot destination: {destinationFilePath}");
        }
        return ExecuteNonQueryAsync($"VACUUM INTO '{destinationFilePath.Replace("'", "''")}';", forMaintenance: false, cancellationToken);
    }

    private static void ValidateDestinationPath(string destinationFilePath)
    {
        if (destinationFilePath == null) { throw new ArgumentNullException(nameof(destinationFilePath)); }
        if (String.IsNullOrWhiteSpace(destinationFilePath)) { throw new ArgumentException("The destination file path cannot be empty or whitespace.", nameof(destinationFilePath)); }
    }

    private void CheckpointWriteAheadLog()
    {
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteScalar();
        }
    }

    private async Task CheckpointWriteAheadLogAsync(CancellationToken cancellationToken)
    {
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void CopyDatabaseTo(string destinationFilePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        using (var destination = new SqliteConnection(builder.ConnectionString))
        {
            destination.Open();
            _connection.BackupDatabase(destination);
            destination.Close();
            SqliteConnection.ClearPool(destination);
        }
    }

    #endregion

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Closes the underlying connection, releases its pooled file handles, and disposes it.
    /// The crypt engine (when one was provided) is owned by the caller and is not disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        AmbientDatabases.Remove(_connection);
        if (_connection.State != ConnectionState.Closed) { _connection.Close(); }
        SqliteConnection.ClearPool(_connection);
        _connection.Dispose();
    }
}
