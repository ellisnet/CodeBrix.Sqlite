using System;
using System.Collections.Generic;
using CodeBrix.Sqlite.Cryptography;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite;

/// <summary>
/// Reads the result sets of a <see cref="SqliteMapper.QueryMultiple"/> batch in order — the
/// Dapper-style grid reader. Each call to <see cref="Read{T}"/> materializes the current result
/// set (with the same encryption-aware rules as <see cref="SqliteMapper.Query{T}"/>) and advances
/// to the next one.
/// </summary>
public sealed class SqliteGridReader : IDisposable
{
    private readonly SqliteCommand _command;
    private readonly SqliteDataReader _reader;
    private readonly IObjectCryptEngine _cryptEngine;
    private readonly SqliteConnection _connectionToClose;
    private bool _isConsumed;
    private bool _disposed;

    internal SqliteGridReader(SqliteCommand command, SqliteDataReader reader, IObjectCryptEngine cryptEngine, SqliteConnection connectionToClose)
    {
        _command = command;
        _reader = reader;
        _cryptEngine = cryptEngine;
        _connectionToClose = connectionToClose;
    }

    /// <summary>
    /// True when every result set of the batch has been read.
    /// </summary>
    public bool IsConsumed => _isConsumed;

    /// <summary>
    /// Materializes the current result set as <typeparamref name="T"/> rows and advances to the
    /// next result set.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="SqliteMapper.Query{T}"/>).</typeparam>
    /// <returns>The materialized rows of the current result set (buffered).</returns>
    /// <exception cref="InvalidOperationException">Thrown when all result sets have already been consumed.</exception>
    public IEnumerable<T> Read<T>()
    {
        ThrowIfDisposed();
        if (_isConsumed) { throw new InvalidOperationException("All result sets of this grid reader have already been consumed."); }
        var rows = new List<T>();
        while (_reader.Read()) { rows.Add(SqliteMapper.MaterializeRow<T>(_reader, _cryptEngine)); }
        if (!_reader.NextResult()) { _isConsumed = true; }
        return rows;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Disposes the underlying reader and command, and closes the connection when it was opened by
    /// the QueryMultiple call that created this grid reader.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _reader.Dispose();
        _command.Dispose();
        _connectionToClose?.Close();
    }
}
