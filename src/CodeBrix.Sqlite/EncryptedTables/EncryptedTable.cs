/*
   Copyright 2014 Ellisnet - Jeremy Ellis (jeremy@ellisnet.com)

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Exceptions;
using CodeBrix.Sqlite.Extensions;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite.EncryptedTables; //was previously: Portable.Data.Sqlite;

/// <summary>
/// A SQLite table, featuring encrypted values, for the specified object type. Item properties are
/// mapped by attribute: <see cref="NotEncryptedAttribute"/> properties get real plaintext columns,
/// <see cref="SearchableAttribute"/> properties stay encrypted but join the in-memory searchable
/// index (stored in the Encrypted_Searchable column), <see cref="BlindIndexedAttribute"/>
/// properties additionally get a deterministic HMAC blind-index column for equality searches, and
/// every property travels inside the whole-object Encrypted_Object column. Added, updated and
/// removed items accumulate in the in-memory <see cref="TempItems"/> cache until
/// <see cref="WriteItemChanges"/> (or disposal, when <see cref="WriteChangesOnDispose"/> is true)
/// persists them.
/// </summary>
/// <typeparam name="T">The type of object to be stored in the table.</typeparam>
public class EncryptedTable<T> : IDisposable where T : EncryptedTableItem, new()
{
    private const string IdColumnName = "Id";
    private const string SearchableColumnName = "Encrypted_Searchable";
    private const string ObjectColumnName = "Encrypted_Object";
    private const string BlindIndexColumnPrefix = "BlindIndex_";

    private static readonly object SetupLock = new object();
    private static bool _typeMapped;
    private static List<(PropertyInfo Property, TableColumn Column)> _notEncryptedColumns;
    private static List<PropertyInfo> _searchableProperties;
    private static List<PropertyInfo> _blindIndexedProperties;

    private readonly SqliteDatabase _database;
    private readonly IObjectCryptEngine _cryptEngine;
    private readonly object _itemsLock = new object();
    private readonly List<T> _tempItems = new List<T>();
    private readonly string _tableName;
    private TableIndex _fullTableIndex;
    private int _indexLifetimeSeconds = 600;
    private long _nextTempId = -1;
    private bool _writeChangesOnDispose = true;
    private bool _disposed;

    #region Public properties

    /// <summary>
    /// The name of the SQLite table.
    /// </summary>
    public string TableName => _tableName;

    /// <summary>
    /// The database the table lives in.
    /// </summary>
    public SqliteDatabase Database => _database;

    /// <summary>
    /// The in-memory cache of items that are currently being manipulated. Items accumulate here
    /// via <see cref="AddItem"/>, <see cref="UpdateItem"/> and <see cref="RemoveItem"/> until
    /// <see cref="WriteItemChanges"/> persists them.
    /// </summary>
    public List<T> TempItems => _tempItems;

    /// <summary>
    /// A COPY (clone) of the in-memory index of the items stored in the table, featuring values of
    /// item properties marked <see cref="SearchableAttribute"/> and <see cref="NotEncryptedAttribute"/>.
    /// Accessing this property builds (or rebuilds) the index when it is missing or expired.
    /// </summary>
    public TableIndex FullTableIndex
    {
        get
        {
            EnsureFullTableIndex();
            lock (_itemsLock)
            {
                return _fullTableIndex?.Clone();
            }
        }
    }

    /// <summary>
    /// The default time-to-live, in seconds, of the in-memory indexes created for this table object.
    /// A value of 0 forces a rebuild on every indexed operation; negative values are clamped to 0.
    /// </summary>
    public int IndexLifetimeSeconds
    {
        get => _indexLifetimeSeconds;
        set
        {
            _indexLifetimeSeconds = (value < 0) ? 0 : value;
            lock (_itemsLock)
            {
                if (_fullTableIndex != null) { _fullTableIndex.LifetimeSeconds = _indexLifetimeSeconds; }
            }
        }
    }

    /// <summary>
    /// When true (the default), pending changes in <see cref="TempItems"/> are written to the
    /// table when the table object is disposed.
    /// </summary>
    public bool WriteChangesOnDispose
    {
        get => _writeChangesOnDispose;
        set => _writeChangesOnDispose = value;
    }

    /// <summary>
    /// A dictionary of the SQLite columns of the table associated with this encrypted table
    /// object, keyed by column name.
    /// </summary>
    public Dictionary<string, TableColumn> TableColumns
    {
        get
        {
            var result = new Dictionary<string, TableColumn>
            {
                [IdColumnName] = new TableColumn { ColumnName = IdColumnName, DataType = "INTEGER", IsNotNull = true }
            };
            foreach ((PropertyInfo Property, TableColumn Column) mapped in _notEncryptedColumns)
            {
                result[mapped.Column.ColumnName] = mapped.Column;
            }
            foreach (PropertyInfo property in _blindIndexedProperties)
            {
                string columnName = BlindIndexColumnPrefix + property.Name;
                result[columnName] = new TableColumn { ColumnName = columnName, PropertyName = property.Name, DataType = "TEXT" };
            }
            result[SearchableColumnName] = new TableColumn { ColumnName = SearchableColumnName, DataType = "TEXT" };
            result[ObjectColumnName] = new TableColumn { ColumnName = ObjectColumnName, DataType = "TEXT" };
            return result;
        }
    }

    #endregion

    #region Ctor

    /// <summary>
    /// Creates an instance of the encrypted table object, using the crypt engine associated with
    /// the database.
    /// </summary>
    /// <param name="database">The database the table lives in.</param>
    /// <param name="checkDbTable">Check that the associated SQLite table exists, creating it (and
    /// its blind indexes) when necessary; the default is true.</param>
    /// <param name="tableName">A desired name for the SQLite table, instead of the name of the item type.</param>
    public EncryptedTable(SqliteDatabase database, bool checkDbTable = true, string tableName = null)
        : this(database?.CryptEngine, database, checkDbTable, tableName)
    {
    }

    /// <summary>
    /// Creates an instance of the encrypted table object with an explicit crypt engine.
    /// </summary>
    /// <param name="cryptEngine">The crypt engine used to encrypt and decrypt item data.</param>
    /// <param name="database">The database the table lives in.</param>
    /// <param name="checkDbTable">Check that the associated SQLite table exists, creating it (and
    /// its blind indexes) when necessary; the default is true.</param>
    /// <param name="tableName">A desired name for the SQLite table, instead of the name of the item type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when no crypt engine is available, when the
    /// table name is invalid, when the item type's attributes are contradictory, or when the item
    /// type has <see cref="BlindIndexedAttribute"/> properties but the crypt engine does not
    /// implement <see cref="IBlindIndexProvider"/>.</exception>
    public EncryptedTable(IObjectCryptEngine cryptEngine, SqliteDatabase database, bool checkDbTable = true, string tableName = null)
    {
        if (database == null) { throw new ArgumentNullException(nameof(database)); }
        _database = database;
        _cryptEngine = cryptEngine ?? throw new EncryptedTableException(
            "No cryptography engine was provided for this encrypted table - provide one on the SqliteDatabase or via the constructor.");
        string candidateName = (String.IsNullOrWhiteSpace(tableName) ? typeof(T).Name : tableName).Trim().Replace(".", "_");
        if (!TableColumn.IsValidIdentifier(candidateName))
        {
            throw new EncryptedTableException(
                $"The specified table name is invalid - '{candidateName}' - table names must start with a letter and may only contain letters, numbers and underscores.");
        }
        _tableName = candidateName;
        EnsureTypeMapped();
        if (_blindIndexedProperties.Count > 0 && !(_cryptEngine is IBlindIndexProvider))
        {
            throw new EncryptedTableException(
                $"The item type '{typeof(T).Name}' has [BlindIndexed] properties, but the crypt engine does not implement IBlindIndexProvider.");
        }
        if (checkDbTable) { CheckDbTable(); }
    }

    #endregion

    #region Type mapping

    private static void EnsureTypeMapped()
    {
        lock (SetupLock)
        {
            if (_typeMapped) { return; }
            var notEncrypted = new List<(PropertyInfo Property, TableColumn Column)>();
            var searchable = new List<PropertyInfo>();
            var blindIndexed = new List<PropertyInfo>();
            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                IdColumnName, SearchableColumnName, ObjectColumnName
            };
            PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (property.DeclaringType == typeof(EncryptedTableItem)) { continue; }
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) { continue; }
                bool isNotEncrypted = property.GetCustomAttribute<NotEncryptedAttribute>() != null;
                bool isSearchable = property.GetCustomAttribute<SearchableAttribute>() != null;
                bool isBlindIndexed = property.GetCustomAttribute<BlindIndexedAttribute>() != null;
                if (isNotEncrypted && isBlindIndexed)
                {
                    throw new EncryptedTableException(
                        $"Property '{property.Name}' of '{typeof(T).Name}' is marked both [NotEncrypted] and [BlindIndexed] - a plaintext column does not need a blind index.");
                }
                if (isNotEncrypted)
                {
                    ColumnNameAttribute nameAttribute = property.GetCustomAttribute<ColumnNameAttribute>();
                    string columnName = nameAttribute?.Name ?? property.Name;
                    if (!reservedNames.Add(columnName))
                    {
                        throw new EncryptedTableException(
                            $"Property '{property.Name}' of '{typeof(T).Name}' maps to column name '{columnName}', which is reserved or already in use.");
                    }
                    notEncrypted.Add((property, new TableColumn
                    {
                        ColumnName = columnName,
                        PropertyName = property.Name,
                        DataType = GetSqliteDataType(property.PropertyType),
                        IsNotNull = property.GetCustomAttribute<NotNullAttribute>() != null,
                        DefaultValue = property.GetCustomAttribute<ColumnDefaultValueAttribute>()?.Value
                    }));
                }
                if (isSearchable || isNotEncrypted) { searchable.Add(property); }
                if (isBlindIndexed) { blindIndexed.Add(property); }
            }
            _notEncryptedColumns = notEncrypted;
            _searchableProperties = searchable;
            _blindIndexedProperties = blindIndexed;
            _typeMapped = true;
        }
    }

    private static string GetSqliteDataType(Type propertyType)
    {
        Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type.IsEnum) { return "INTEGER"; }
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(int)
            || type == typeof(uint) || type == typeof(long))
        {
            return "INTEGER";
        }
        if (type == typeof(float) || type == typeof(double)) { return "REAL"; }
        if (type == typeof(byte[])) { return "BLOB"; }
        return "TEXT";
    }

    #endregion

    #region Table creation

    /// <summary>
    /// Checks that the SQLite table (and the blind indexes of any <see cref="BlindIndexedAttribute"/>
    /// properties) exist, creating them when necessary.
    /// </summary>
    public void CheckDbTable()
    {
        ThrowIfDisposed();
        _database.ExecuteNonQuery(BuildCreateTableSql());
        foreach (string indexSql in BuildCreateIndexSql())
        {
            _database.ExecuteNonQuery(indexSql);
        }
    }

    private string BuildCreateTableSql()
    {
        var sql = new StringBuilder();
        sql.Append($"CREATE TABLE IF NOT EXISTS [{_tableName}] ([{IdColumnName}] INTEGER PRIMARY KEY AUTOINCREMENT");
        foreach ((PropertyInfo Property, TableColumn Column) mapped in _notEncryptedColumns)
        {
            sql.Append($", [{mapped.Column.ColumnName}] {mapped.Column.DataType}");
            if (mapped.Column.IsNotNull) { sql.Append(" NOT NULL"); }
            if (mapped.Column.DefaultValue != null) { sql.Append($" DEFAULT '{mapped.Column.DefaultValue}'"); }
        }
        foreach (PropertyInfo property in _blindIndexedProperties)
        {
            sql.Append($", [{BlindIndexColumnPrefix}{property.Name}] TEXT");
        }
        sql.Append($", [{SearchableColumnName}] TEXT, [{ObjectColumnName}] TEXT);");
        return sql.ToString();
    }

    private List<string> BuildCreateIndexSql()
        => _blindIndexedProperties
            .Select(p => $"CREATE INDEX IF NOT EXISTS [IX_{_tableName}_{BlindIndexColumnPrefix}{p.Name}] ON [{_tableName}]([{BlindIndexColumnPrefix}{p.Name}]);")
            .ToList();

    #endregion

    #region Item cache operations

    /// <summary>
    /// Adds a new item to the in-memory cache, assigning it a temporary (negative) id. The item is
    /// written to the table by <see cref="WriteItemChanges"/> — or immediately, when
    /// <paramref name="immediateWriteToTable"/> is true.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="immediateWriteToTable">True to write all pending changes (including this item) to the table now.</param>
    /// <returns>The id assigned to the item: the real row id when written immediately, otherwise the temporary id.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when the item has already been written to the table or is already tracked.</exception>
    public long AddItem(T item, bool immediateWriteToTable = false)
    {
        ThrowIfDisposed();
        if (item == null) { throw new ArgumentNullException(nameof(item)); }
        lock (_itemsLock)
        {
            if (item.Id > 0)
            {
                throw new EncryptedTableException(
                    $"The item already has row id {item.Id} and appears to have been written to the table - use UpdateItem() instead.");
            }
            if (_tempItems.Contains(item))
            {
                throw new EncryptedTableException("The item has already been added to this table's item cache.");
            }
            item.SyncStatus = TableItemStatus.New;
            item.Id = _nextTempId--;
            _tempItems.Add(item);
        }
        if (immediateWriteToTable) { WriteItemChanges(); }
        return item.Id;
    }

    /// <summary>
    /// Marks an item as modified so its current property values are written to the table by
    /// <see cref="WriteItemChanges"/>. An item that is not yet tracked in <see cref="TempItems"/>
    /// is attached, provided it has a real row id.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when the item has never been written to the table.</exception>
    public void UpdateItem(T item)
    {
        ThrowIfDisposed();
        if (item == null) { throw new ArgumentNullException(nameof(item)); }
        lock (_itemsLock)
        {
            if (_tempItems.Contains(item))
            {
                if (item.SyncStatus != TableItemStatus.New) { item.SyncStatus = TableItemStatus.Modified; }
                return;
            }
            if (item.Id <= 0)
            {
                throw new EncryptedTableException("The item has not been written to the table yet - use AddItem() instead.");
            }
            item.SyncStatus = TableItemStatus.Modified;
            _tempItems.Add(item);
        }
    }

    /// <summary>
    /// Marks the item with the specified id for deletion from the table. A tracked item that was
    /// never written is simply dropped from the cache.
    /// </summary>
    /// <param name="itemId">The id of the item to remove; may be the temporary id of an unwritten item.</param>
    /// <param name="immediateWriteToTable">True to write all pending changes (including this removal) to the table now.</param>
    /// <exception cref="EncryptedTableException">Thrown when the id is not tracked and is not a real row id.</exception>
    public void RemoveItem(long itemId, bool immediateWriteToTable = false)
    {
        ThrowIfDisposed();
        lock (_itemsLock)
        {
            T tracked = _tempItems.FirstOrDefault(i => i.Id == itemId);
            if (tracked != null)
            {
                if (tracked.SyncStatus == TableItemStatus.New)
                {
                    _tempItems.Remove(tracked);
                }
                else
                {
                    tracked.SyncStatus = TableItemStatus.DeletePending;
                }
            }
            else
            {
                if (itemId <= 0)
                {
                    throw new EncryptedTableException(
                        $"Item id {itemId} is not tracked in the item cache and is not a real table row id.");
                }
                var stub = new T { Id = itemId, SyncStatus = TableItemStatus.DeletePending };
                _tempItems.Add(stub);
            }
        }
        if (immediateWriteToTable) { WriteItemChanges(); }
    }

    #endregion

    #region Writing changes

    /// <summary>
    /// Writes all pending changes in <see cref="TempItems"/> to the table: new items are inserted
    /// (and receive their real row ids), modified items are updated, and items marked for deletion
    /// are deleted and dropped from the cache.
    /// </summary>
    /// <returns>The number of table rows inserted, updated or deleted.</returns>
    public int WriteItemChanges()
    {
        ThrowIfDisposed();
        _database.SafeOpen();
        int changes = 0;
        foreach (T item in SnapshotTempItems())
        {
            switch (item.SyncStatus)
            {
                case TableItemStatus.New:
                    using (SqliteCommand command = BuildInsertCommand(item))
                    {
                        item.Id = command.ExecuteReturnRowId();
                    }
                    item.SyncStatus = TableItemStatus.Unchanged;
                    changes++;
                    break;
                case TableItemStatus.Modified:
                    EnsureRealRowId(item);
                    using (SqliteCommand command = BuildUpdateCommand(item))
                    {
                        command.ExecuteNonQuery();
                    }
                    item.SyncStatus = TableItemStatus.Unchanged;
                    changes++;
                    break;
                case TableItemStatus.DeletePending:
                    using (SqliteCommand command = BuildDeleteCommand(item.Id))
                    {
                        command.ExecuteNonQuery();
                    }
                    lock (_itemsLock) { _tempItems.Remove(item); }
                    changes++;
                    break;
            }
        }
        if (changes > 0) { DropFullTableIndex(); }
        return changes;
    }

    /// <summary>
    /// Asynchronously writes all pending changes in <see cref="TempItems"/> to the table.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the number of table rows inserted, updated or deleted.</returns>
    public async Task<int> WriteItemChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _database.SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        int changes = 0;
        foreach (T item in SnapshotTempItems())
        {
            switch (item.SyncStatus)
            {
                case TableItemStatus.New:
                    using (SqliteCommand command = BuildInsertCommand(item))
                    {
                        item.Id = await command.ExecuteReturnRowIdAsync(cancellationToken).ConfigureAwait(false);
                    }
                    item.SyncStatus = TableItemStatus.Unchanged;
                    changes++;
                    break;
                case TableItemStatus.Modified:
                    EnsureRealRowId(item);
                    using (SqliteCommand command = BuildUpdateCommand(item))
                    {
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    item.SyncStatus = TableItemStatus.Unchanged;
                    changes++;
                    break;
                case TableItemStatus.DeletePending:
                    using (SqliteCommand command = BuildDeleteCommand(item.Id))
                    {
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    lock (_itemsLock) { _tempItems.Remove(item); }
                    changes++;
                    break;
            }
        }
        if (changes > 0) { DropFullTableIndex(); }
        return changes;
    }

    private T[] SnapshotTempItems()
    {
        lock (_itemsLock)
        {
            return _tempItems.ToArray();
        }
    }

    private static void EnsureRealRowId(T item)
    {
        if (item.Id <= 0)
        {
            throw new EncryptedTableException(
                $"Cannot update the item with temporary id {item.Id} - it has not been inserted yet.");
        }
    }

    private SqliteCommand BuildInsertCommand(T item)
    {
        var columns = new StringBuilder();
        var values = new StringBuilder();
        SqliteCommand command = _database.CreateCommand();
        int index = 0;
        foreach ((PropertyInfo Property, TableColumn Column) mapped in _notEncryptedColumns)
        {
            columns.Append($"[{mapped.Column.ColumnName}], ");
            values.Append($"@c{index}, ");
            command.Parameters.AddWithValue($"@c{index}", ToParameterValue(mapped.Property.GetValue(item)));
            index++;
        }
        index = 0;
        foreach (PropertyInfo property in _blindIndexedProperties)
        {
            columns.Append($"[{BlindIndexColumnPrefix}{property.Name}], ");
            values.Append($"@b{index}, ");
            command.Parameters.AddWithValue($"@b{index}", (object)ComputeBlindIndexValue(property, item) ?? DBNull.Value);
            index++;
        }
        columns.Append($"[{SearchableColumnName}], [{ObjectColumnName}]");
        values.Append("@searchable, @object");
        command.Parameters.AddWithValue("@searchable", _cryptEngine.EncryptObject(BuildSearchableIndexValues(item)));
        command.Parameters.AddWithValue("@object", _cryptEngine.EncryptObject(item));
        command.CommandText = $"INSERT INTO [{_tableName}] ({columns}) VALUES ({values});";
        return command;
    }

    private SqliteCommand BuildUpdateCommand(T item)
    {
        var assignments = new StringBuilder();
        SqliteCommand command = _database.CreateCommand();
        int index = 0;
        foreach ((PropertyInfo Property, TableColumn Column) mapped in _notEncryptedColumns)
        {
            assignments.Append($"[{mapped.Column.ColumnName}] = @c{index}, ");
            command.Parameters.AddWithValue($"@c{index}", ToParameterValue(mapped.Property.GetValue(item)));
            index++;
        }
        index = 0;
        foreach (PropertyInfo property in _blindIndexedProperties)
        {
            assignments.Append($"[{BlindIndexColumnPrefix}{property.Name}] = @b{index}, ");
            command.Parameters.AddWithValue($"@b{index}", (object)ComputeBlindIndexValue(property, item) ?? DBNull.Value);
            index++;
        }
        assignments.Append($"[{SearchableColumnName}] = @searchable, [{ObjectColumnName}] = @object");
        command.Parameters.AddWithValue("@searchable", _cryptEngine.EncryptObject(BuildSearchableIndexValues(item)));
        command.Parameters.AddWithValue("@object", _cryptEngine.EncryptObject(item));
        command.Parameters.AddWithValue("@id", item.Id);
        command.CommandText = $"UPDATE [{_tableName}] SET {assignments} WHERE [{IdColumnName}] = @id;";
        return command;
    }

    private SqliteCommand BuildDeleteCommand(long itemId)
    {
        SqliteCommand command = _database.CreateCommand($"DELETE FROM [{_tableName}] WHERE [{IdColumnName}] = @id;");
        command.Parameters.AddWithValue("@id", itemId);
        return command;
    }

    private string ComputeBlindIndexValue(PropertyInfo property, T item)
        => ((IBlindIndexProvider)_cryptEngine).ComputeBlindIndex(ValueToString(property.GetValue(item)));

    private Dictionary<string, string> BuildSearchableIndexValues(T item)
    {
        var result = new Dictionary<string, string>();
        foreach (PropertyInfo property in _searchableProperties)
        {
            result[property.Name] = ValueToString(property.GetValue(item));
        }
        return result;
    }

    private static string ValueToString(object value)
        => value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static object ToParameterValue(object value)
    {
        if (value == null) { return DBNull.Value; }
        if (value is Enum) { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
        return value;
    }

    #endregion

    #region Reading items

    /// <summary>
    /// Retrieves the item with the specified id, checking the in-memory cache first and then the
    /// table.
    /// </summary>
    /// <param name="itemId">The id of the item to retrieve.</param>
    /// <param name="exceptionOnMissingItem">True to throw when the item does not exist; when false
    /// (the default), null is returned instead.</param>
    /// <returns>The item, or null when it does not exist and <paramref name="exceptionOnMissingItem"/> is false.</returns>
    /// <exception cref="EncryptedTableException">Thrown when the item does not exist and <paramref name="exceptionOnMissingItem"/> is true.</exception>
    public T GetItem(long itemId, bool exceptionOnMissingItem = false)
    {
        ThrowIfDisposed();
        T tracked = FindTrackedItem(itemId);
        if (tracked != null) { return tracked; }
        _database.SafeOpen();
        object payload;
        using (SqliteCommand command = BuildSelectObjectCommand(itemId))
        {
            payload = command.ExecuteScalar();
        }
        return MaterializeItem(itemId, payload, exceptionOnMissingItem);
    }

    /// <summary>
    /// Asynchronously retrieves the item with the specified id.
    /// </summary>
    /// <param name="itemId">The id of the item to retrieve.</param>
    /// <param name="exceptionOnMissingItem">True to throw when the item does not exist; when false
    /// (the default), null is returned instead.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the item, or null when it does not exist and <paramref name="exceptionOnMissingItem"/> is false.</returns>
    /// <exception cref="EncryptedTableException">Thrown when the item does not exist and <paramref name="exceptionOnMissingItem"/> is true.</exception>
    public async Task<T> GetItemAsync(long itemId, bool exceptionOnMissingItem = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        T tracked = FindTrackedItem(itemId);
        if (tracked != null) { return tracked; }
        await _database.SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        object payload;
        using (SqliteCommand command = BuildSelectObjectCommand(itemId))
        {
            payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        return MaterializeItem(itemId, payload, exceptionOnMissingItem);
    }

    /// <summary>
    /// Retrieves the items matching the specified search. The search is evaluated against the
    /// in-memory searchable index (built by decrypting only the Encrypted_Searchable column), and
    /// only matching items have their full objects decrypted.
    /// </summary>
    /// <param name="search">The search to evaluate.</param>
    /// <param name="writeChangesFirst">True (the default) to write pending item changes to the
    /// table first, so they are visible to the search.</param>
    /// <returns>The matching items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="search"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when a search criterion names a property
    /// that is not searchable.</exception>
    public List<T> GetItems(TableSearch search, bool writeChangesFirst = true)
    {
        ThrowIfDisposed();
        if (search == null) { throw new ArgumentNullException(nameof(search)); }
        ValidateSearchProperties(search);
        if (writeChangesFirst) { WriteItemChanges(); }
        EnsureFullTableIndex();
        List<long> matchingIds = GetMatchingIds(search);
        return FetchItemsByIds(matchingIds);
    }

    /// <summary>
    /// Asynchronously retrieves the items matching the specified search.
    /// </summary>
    /// <param name="search">The search to evaluate.</param>
    /// <param name="writeChangesFirst">True (the default) to write pending item changes to the
    /// table first, so they are visible to the search.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the matching items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="search"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when a search criterion names a property
    /// that is not searchable.</exception>
    public async Task<List<T>> GetItemsAsync(TableSearch search, bool writeChangesFirst = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (search == null) { throw new ArgumentNullException(nameof(search)); }
        ValidateSearchProperties(search);
        if (writeChangesFirst) { await WriteItemChangesAsync(cancellationToken).ConfigureAwait(false); }
        await EnsureFullTableIndexAsync(cancellationToken).ConfigureAwait(false);
        List<long> matchingIds = GetMatchingIds(search);
        return await FetchItemsByIdsAsync(matchingIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the items whose <see cref="BlindIndexedAttribute"/> property exactly equals the
    /// specified value, using the deterministic HMAC blind-index column — a SQL-indexed equality
    /// search that never decrypts non-matching rows. Matching is exact and case-sensitive.
    /// </summary>
    /// <param name="propertyName">The name of the blind-indexed property.</param>
    /// <param name="value">The plaintext value to search for.</param>
    /// <param name="writeChangesFirst">True (the default) to write pending item changes to the
    /// table first, so they are visible to the search.</param>
    /// <returns>The matching items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="propertyName"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when the property is not marked <see cref="BlindIndexedAttribute"/>.</exception>
    public List<T> FindByBlindIndex(string propertyName, string value, bool writeChangesFirst = true)
    {
        ThrowIfDisposed();
        string blindIndexValue = PrepareBlindIndexSearch(propertyName, value);
        if (writeChangesFirst) { WriteItemChanges(); }
        _database.SafeOpen();
        var results = new List<T>();
        using (SqliteCommand command = BuildBlindIndexCommand(propertyName, blindIndexValue))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                results.Add(DecryptItem(reader.GetInt64(0), reader.GetString(1)));
            }
        }
        return results;
    }

    /// <summary>
    /// Asynchronously retrieves the items whose <see cref="BlindIndexedAttribute"/> property
    /// exactly equals the specified value, using the deterministic HMAC blind-index column.
    /// </summary>
    /// <param name="propertyName">The name of the blind-indexed property.</param>
    /// <param name="value">The plaintext value to search for.</param>
    /// <param name="writeChangesFirst">True (the default) to write pending item changes to the
    /// table first, so they are visible to the search.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the matching items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="propertyName"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="EncryptedTableException">Thrown when the property is not marked <see cref="BlindIndexedAttribute"/>.</exception>
    public async Task<List<T>> FindByBlindIndexAsync(string propertyName, string value, bool writeChangesFirst = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string blindIndexValue = PrepareBlindIndexSearch(propertyName, value);
        if (writeChangesFirst) { await WriteItemChangesAsync(cancellationToken).ConfigureAwait(false); }
        await _database.SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<T>();
        using (SqliteCommand command = BuildBlindIndexCommand(propertyName, blindIndexValue))
        using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(DecryptItem(reader.GetInt64(0), reader.GetString(1)));
            }
        }
        return results;
    }

    private string PrepareBlindIndexSearch(string propertyName, string value)
    {
        if (propertyName == null) { throw new ArgumentNullException(nameof(propertyName)); }
        if (value == null) { throw new ArgumentNullException(nameof(value)); }
        if (!_blindIndexedProperties.Any(p => p.Name == propertyName))
        {
            throw new EncryptedTableException(
                $"Property '{propertyName}' of '{typeof(T).Name}' is not marked [BlindIndexed] - blind-index searches require the attribute.");
        }
        return ((IBlindIndexProvider)_cryptEngine).ComputeBlindIndex(value);
    }

    private SqliteCommand BuildBlindIndexCommand(string propertyName, string blindIndexValue)
    {
        SqliteCommand command = _database.CreateCommand(
            $"SELECT [{IdColumnName}], [{ObjectColumnName}] FROM [{_tableName}] WHERE [{BlindIndexColumnPrefix}{propertyName}] = @blindIndex;");
        command.Parameters.AddWithValue("@blindIndex", blindIndexValue);
        return command;
    }

    private T FindTrackedItem(long itemId)
    {
        lock (_itemsLock)
        {
            return _tempItems.FirstOrDefault(i => i.Id == itemId);
        }
    }

    private SqliteCommand BuildSelectObjectCommand(long itemId)
    {
        SqliteCommand command = _database.CreateCommand(
            $"SELECT [{ObjectColumnName}] FROM [{_tableName}] WHERE [{IdColumnName}] = @id;");
        command.Parameters.AddWithValue("@id", itemId);
        return command;
    }

    private T MaterializeItem(long itemId, object payload, bool exceptionOnMissingItem)
    {
        if (payload == null || payload == DBNull.Value)
        {
            if (exceptionOnMissingItem)
            {
                throw new EncryptedTableException($"No item with id {itemId} exists in table '{_tableName}'.");
            }
            return null;
        }
        return DecryptItem(itemId, (string)payload);
    }

    private T DecryptItem(long itemId, string encryptedPayload)
    {
        T item = _cryptEngine.DecryptObject<T>(encryptedPayload);
        item.Id = itemId;
        item.SyncStatus = TableItemStatus.Unchanged;
        return item;
    }

    private List<T> FetchItemsByIds(List<long> itemIds)
    {
        var results = new List<T>();
        if (itemIds.Count == 0) { return results; }
        _database.SafeOpen();
        using (SqliteCommand command = BuildSelectByIdsCommand(itemIds))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                results.Add(DecryptItem(reader.GetInt64(0), reader.GetString(1)));
            }
        }
        return results;
    }

    private async Task<List<T>> FetchItemsByIdsAsync(List<long> itemIds, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        if (itemIds.Count == 0) { return results; }
        await _database.SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        using (SqliteCommand command = BuildSelectByIdsCommand(itemIds))
        using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(DecryptItem(reader.GetInt64(0), reader.GetString(1)));
            }
        }
        return results;
    }

    private SqliteCommand BuildSelectByIdsCommand(List<long> itemIds)
    {
        SqliteCommand command = _database.CreateCommand();
        var parameterNames = new List<string>();
        for (int i = 0; i < itemIds.Count; i++)
        {
            string parameterName = $"@i{i}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, itemIds[i]);
        }
        command.CommandText =
            $"SELECT [{IdColumnName}], [{ObjectColumnName}] FROM [{_tableName}] WHERE [{IdColumnName}] IN ({String.Join(", ", parameterNames)});";
        return command;
    }

    #endregion

    #region Searchable index

    /// <summary>
    /// Builds (or rebuilds) the in-memory searchable index by decrypting the Encrypted_Searchable
    /// column of every row.
    /// </summary>
    /// <returns>The number of table rows indexed.</returns>
    public int BuildFullTableIndex()
    {
        ThrowIfDisposed();
        _database.SafeOpen();
        var index = new TableIndex { LifetimeSeconds = _indexLifetimeSeconds };
        using (SqliteCommand command = _database.CreateCommand(
            $"SELECT [{IdColumnName}], [{SearchableColumnName}] FROM [{_tableName}];"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                index.Items[reader.GetInt64(0)] = DecryptSearchableValues(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            }
        }
        lock (_itemsLock) { _fullTableIndex = index; }
        return index.Items.Count;
    }

    /// <summary>
    /// Asynchronously builds (or rebuilds) the in-memory searchable index.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task producing the number of table rows indexed.</returns>
    public async Task<int> BuildFullTableIndexAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _database.SafeOpenAsync(cancellationToken).ConfigureAwait(false);
        var index = new TableIndex { LifetimeSeconds = _indexLifetimeSeconds };
        using (SqliteCommand command = _database.CreateCommand(
            $"SELECT [{IdColumnName}], [{SearchableColumnName}] FROM [{_tableName}];"))
        using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                index.Items[reader.GetInt64(0)] = DecryptSearchableValues(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            }
        }
        lock (_itemsLock) { _fullTableIndex = index; }
        return index.Items.Count;
    }

    /// <summary>
    /// Discards the in-memory searchable index, forcing a rebuild on the next indexed operation.
    /// </summary>
    public void DropFullTableIndex()
    {
        lock (_itemsLock) { _fullTableIndex = null; }
    }

    /// <summary>
    /// Checks whether a current (non-expired) in-memory searchable index exists, optionally
    /// rebuilding it when missing or expired.
    /// </summary>
    /// <param name="rebuildIfExpired">True to rebuild a missing or expired index.</param>
    /// <returns>True when a current index exists after the call.</returns>
    public bool CheckFullTableIndex(bool rebuildIfExpired = false)
    {
        ThrowIfDisposed();
        bool isCurrent;
        lock (_itemsLock)
        {
            isCurrent = _fullTableIndex != null && !_fullTableIndex.IsExpired;
        }
        if (!isCurrent && rebuildIfExpired)
        {
            BuildFullTableIndex();
            return true;
        }
        return isCurrent;
    }

    private void EnsureFullTableIndex()
        => CheckFullTableIndex(rebuildIfExpired: true);

    private async Task EnsureFullTableIndexAsync(CancellationToken cancellationToken)
    {
        bool isCurrent;
        lock (_itemsLock)
        {
            isCurrent = _fullTableIndex != null && !_fullTableIndex.IsExpired;
        }
        if (!isCurrent)
        {
            await BuildFullTableIndexAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Dictionary<string, string> DecryptSearchableValues(long itemId, string encryptedSearchable)
    {
        if (encryptedSearchable == null) { return new Dictionary<string, string>(); }
        try
        {
            return _cryptEngine.DecryptObject<Dictionary<string, string>>(encryptedSearchable)
                ?? new Dictionary<string, string>();
        }
        catch (ObjectCryptographyException ex)
        {
            throw new EncryptedTableException(
                $"Unable to decrypt the '{SearchableColumnName}' column for the record with id {itemId}.", ex);
        }
    }

    private void ValidateSearchProperties(TableSearch search)
    {
        foreach (TableSearchItem matchItem in search.MatchItems)
        {
            if (!_searchableProperties.Any(p => p.Name == matchItem.PropertyName))
            {
                throw new EncryptedTableException(
                    $"Property '{matchItem.PropertyName}' of '{typeof(T).Name}' is not searchable - mark it [Searchable] or [NotEncrypted] to include it in searches.");
            }
        }
    }

    private List<long> GetMatchingIds(TableSearch search)
    {
        lock (_itemsLock)
        {
            return _fullTableIndex == null
                ? new List<long>()
                : _fullTableIndex.Items.Where(i => MatchesSearch(i.Value, search)).Select(i => i.Key).ToList();
        }
    }

    private static bool MatchesSearch(Dictionary<string, string> values, TableSearch search)
    {
        if (search.MatchItems.Count == 0) { return search.SearchType == TableSearchType.MatchAll; }
        return search.SearchType == TableSearchType.MatchAll
            ? search.MatchItems.All(m => EvaluateCriterion(values, m, search))
            : search.MatchItems.Any(m => EvaluateCriterion(values, m, search));
    }

    private static bool EvaluateCriterion(Dictionary<string, string> values, TableSearchItem matchItem, TableSearch search)
    {
        values.TryGetValue(matchItem.PropertyName, out string current);
        string target = matchItem.Value;
        if (search.TrimValues)
        {
            current = current?.Trim();
            target = target.Trim();
        }
        StringComparison comparison = search.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        switch (matchItem.MatchType)
        {
            case SearchItemMatchType.IsEqualTo:
                return current != null && current.Equals(target, comparison);
            case SearchItemMatchType.IsNotEqualTo:
                return !(current != null && current.Equals(target, comparison));
            case SearchItemMatchType.Contains:
                return current != null && current.Contains(target, comparison);
            case SearchItemMatchType.DoesNotContain:
                return !(current != null && current.Contains(target, comparison));
            case SearchItemMatchType.StartsWith:
                return current != null && current.StartsWith(target, comparison);
            case SearchItemMatchType.EndsWith:
                return current != null && current.EndsWith(target, comparison);
            default:
                return false;
        }
    }

    #endregion

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Writes pending item changes to the table (when <see cref="WriteChangesOnDispose"/> is true)
    /// and releases the table object. The database and crypt engine are owned by the caller and
    /// are not disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        if (_writeChangesOnDispose) { WriteItemChanges(); }
        _disposed = true;
    }
}
