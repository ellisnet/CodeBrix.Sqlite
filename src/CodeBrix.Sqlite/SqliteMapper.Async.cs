using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite;

public static partial class SqliteMapper
{
    /// <summary>
    /// Asynchronously executes a query and materializes each result row as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the materialized rows (buffered).</returns>
    public static async Task<IEnumerable<T>> QueryAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                IObjectCryptEngine engine = ResolveEngine(connection, cryptEngine);
                var rows = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(MaterializeRow<T>((SqliteDataReader)reader, engine));
                }
                return rows;
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Asynchronously executes a query and materializes each result row as a dynamic object.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the materialized rows (buffered).</returns>
    public static async Task<IEnumerable<dynamic>> QueryAsync(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var rows = new List<dynamic>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(MaterializeDynamicRow((SqliteDataReader)reader));
                }
                return rows;
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Asynchronously executes a query and returns the first result row, throwing when the result
    /// set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the first row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty.</exception>
    public static async Task<T> QueryFirstAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
        => (await connection.QueryAsync<T>(sql, param, transaction, cryptEngine, cancellationToken).ConfigureAwait(false)).First();

    /// <summary>
    /// Asynchronously executes a query and returns the first result row, or the type's default
    /// value when the result set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the first row, or default.</returns>
    public static async Task<T> QueryFirstOrDefaultAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
        => (await connection.QueryAsync<T>(sql, param, transaction, cryptEngine, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>
    /// Asynchronously executes a query and returns the single result row, throwing when the result
    /// set is empty or holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the single row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty or has more than one row.</exception>
    public static async Task<T> QuerySingleAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
        => (await connection.QueryAsync<T>(sql, param, transaction, cryptEngine, cancellationToken).ConfigureAwait(false)).Single();

    /// <summary>
    /// Asynchronously executes a query and returns the single result row (or default when empty),
    /// throwing when the result set holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the single row, or default.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set has more than one row.</exception>
    public static async Task<T> QuerySingleOrDefaultAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
        => (await connection.QueryAsync<T>(sql, param, transaction, cryptEngine, cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    /// <summary>
    /// Asynchronously executes a statement (INSERT, UPDATE, DELETE, DDL) and returns the number of
    /// rows affected.
    /// </summary>
    /// <param name="connection">The connection to execute on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters; values wrapped in <see cref="EncryptedValue"/> are encrypted on bind.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the number of rows affected.</returns>
    public static async Task<int> ExecuteAsync(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            {
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Asynchronously executes a query and returns the first column of the first row converted to
    /// <typeparamref name="T"/>, or the type's default value when the result is empty or NULL.
    /// </summary>
    /// <typeparam name="T">The scalar type to convert the value to.</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the converted scalar value.</returns>
    public static async Task<T> ExecuteScalarAsync<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            {
                return ConvertScalar<T>(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Asynchronously executes a query and returns the open data reader. When the connection had to
    /// be opened by this call, the reader closes it again on disposal.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the open data reader.</returns>
    public static async Task<SqliteDataReader> ExecuteReaderAsync(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine);
        return (SqliteDataReader)await command
            .ExecuteReaderAsync(wasClosed ? CommandBehavior.CloseConnection : CommandBehavior.Default, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously executes a batch of statements and returns a <see cref="SqliteGridReader"/>
    /// for reading each result set in turn.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL batch to execute (multiple statements separated by ';').</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the grid reader positioned on the first result set.</returns>
    public static async Task<SqliteGridReader> QueryMultipleAsync(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null, CancellationToken cancellationToken = default)
    {
        bool wasClosed = await PrepareConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine);
        var reader = (SqliteDataReader)await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return new SqliteGridReader(command, reader, ResolveEngine(connection, cryptEngine), wasClosed ? connection : null);
    }

    private static async Task<bool> PrepareConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection == null) { throw new ArgumentNullException(nameof(connection)); }
        GuardMaintenanceMode(connection);
        bool wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); }
        return wasClosed;
    }
}
