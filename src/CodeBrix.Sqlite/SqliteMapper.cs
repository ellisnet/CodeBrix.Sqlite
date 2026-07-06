using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite;

/// <summary>
/// Dapper-style CRUD extension methods for <see cref="SqliteConnection"/>: Query / QueryFirst /
/// QuerySingle (and -OrDefault), Execute, ExecuteScalar, ExecuteReader and QueryMultiple, in sync
/// and async forms, with anonymous-object parameters and IN-list expansion. The API surface
/// mirrors Dapper 2.1.79, with one addition: the methods are aware of CodeBrix.Sqlite encryption.
/// Result types deriving from <see cref="EncryptedTableItem"/> are materialized by decrypting
/// their Encrypted_Object column; POCO properties marked <see cref="EncryptedColumnAttribute"/>
/// are decrypted on read; parameter values wrapped in <see cref="EncryptedValue"/> are encrypted
/// on bind. The crypt engine comes from the optional <c>cryptEngine</c> argument, or ambiently
/// from the <see cref="SqliteDatabase"/> that owns the connection.
/// </summary>
public static partial class SqliteMapper
{
    #region Query (sync)

    /// <summary>
    /// Executes a query and materializes each result row as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The result row type: a simple type (first column), a POCO (columns mapped
    /// to writable properties, case-insensitively and underscore-tolerantly), or an
    /// <see cref="EncryptedTableItem"/> type (decrypted from its Encrypted_Object column).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters: an anonymous object, POCO, or <c>IDictionary&lt;string, object&gt;</c>.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override; when null, the engine of the owning
    /// <see cref="SqliteDatabase"/> (if any) is used.</param>
    /// <returns>The materialized rows (buffered).</returns>
    public static IEnumerable<T> Query<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                return BufferRows<T>(reader, ResolveEngine(connection, cryptEngine));
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and materializes each result row as a dynamic object whose members are the
    /// result columns.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters: an anonymous object, POCO, or <c>IDictionary&lt;string, object&gt;</c>.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The materialized rows (buffered).</returns>
    public static IEnumerable<dynamic> Query(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                var rows = new List<dynamic>();
                while (reader.Read()) { rows.Add(MaterializeDynamicRow(reader)); }
                return rows;
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the first result row as <typeparamref name="T"/>, throwing when
    /// the result set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The first row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty.</exception>
    public static T QueryFirst<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
        => connection.Query<T>(sql, param, transaction, cryptEngine).First();

    /// <summary>
    /// Executes a query and returns the first result row as <typeparamref name="T"/>, or the type's
    /// default value when the result set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The first row, or default.</returns>
    public static T QueryFirstOrDefault<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
        => connection.Query<T>(sql, param, transaction, cryptEngine).FirstOrDefault();

    /// <summary>
    /// Executes a query and returns the single result row as <typeparamref name="T"/>, throwing
    /// when the result set is empty or holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The single row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty or has more than one row.</exception>
    public static T QuerySingle<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
        => connection.Query<T>(sql, param, transaction, cryptEngine).Single();

    /// <summary>
    /// Executes a query and returns the single result row as <typeparamref name="T"/> (or default
    /// when empty), throwing when the result set holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The single row, or default.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set has more than one row.</exception>
    public static T QuerySingleOrDefault<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
        => connection.Query<T>(sql, param, transaction, cryptEngine).SingleOrDefault();

    #endregion

    #region Execute / scalar / reader / multiple (sync)

    /// <summary>
    /// Executes a statement (INSERT, UPDATE, DELETE, DDL) and returns the number of rows affected.
    /// </summary>
    /// <param name="connection">The connection to execute on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters; values wrapped in <see cref="EncryptedValue"/> are encrypted on bind.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The number of rows affected.</returns>
    public static int Execute(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            {
                return command.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the first column of the first row converted to
    /// <typeparamref name="T"/>, or the type's default value when the result is empty or NULL.
    /// </summary>
    /// <typeparam name="T">The scalar type to convert the value to.</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The converted scalar value.</returns>
    public static T ExecuteScalar<T>(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine))
            {
                return ConvertScalar<T>(command.ExecuteScalar());
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the open data reader. When the connection had to be opened by
    /// this call, the reader closes it again on disposal.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The open data reader.</returns>
    public static SqliteDataReader ExecuteReader(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine);
        return command.ExecuteReader(wasClosed ? CommandBehavior.CloseConnection : CommandBehavior.Default);
    }

    /// <summary>
    /// Executes a batch of statements and returns a <see cref="SqliteGridReader"/> for reading each
    /// result set in turn.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL batch to execute (multiple statements separated by ';').</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <param name="cryptEngine">Optional crypt engine override.</param>
    /// <returns>The grid reader positioned on the first result set.</returns>
    public static SqliteGridReader QueryMultiple(this SqliteConnection connection, string sql, object param = null, SqliteTransaction transaction = null, IObjectCryptEngine cryptEngine = null)
    {
        bool wasClosed = PrepareConnection(connection);
        SqliteCommand command = CreateMapperCommand(connection, sql, param, transaction, cryptEngine);
        SqliteDataReader reader = command.ExecuteReader();
        return new SqliteGridReader(command, reader, ResolveEngine(connection, cryptEngine), wasClosed ? connection : null);
    }

    #endregion

    #region Shared internals

    private static bool PrepareConnection(SqliteConnection connection)
    {
        if (connection == null) { throw new ArgumentNullException(nameof(connection)); }
        GuardMaintenanceMode(connection);
        bool wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) { connection.Open(); }
        return wasClosed;
    }

    private static void GuardMaintenanceMode(SqliteConnection connection)
    {
        if (SqliteDatabase.TryGetAmbientDatabase(connection, out SqliteDatabase database) && database.IsInMaintenanceMode)
        {
            throw new DatabaseMaintenanceException(
                "The database is in maintenance mode - normal operations are blocked until EndMaintenanceMode() is called.");
        }
    }

    private static IObjectCryptEngine ResolveEngine(SqliteConnection connection, IObjectCryptEngine explicitEngine)
    {
        if (explicitEngine != null) { return explicitEngine; }
        return SqliteDatabase.TryGetAmbientDatabase(connection, out SqliteDatabase database) ? database.CryptEngine : null;
    }

    private static IObjectCryptEngine RequireEngine(IObjectCryptEngine engine, string reason)
        => engine ?? throw new ObjectCryptographyException(
            $"{reason}, but no crypt engine is available - pass one via the cryptEngine argument, or use the connection of a SqliteDatabase that has one.");

    private static SqliteCommand CreateMapperCommand(SqliteConnection connection, string sql, object param, SqliteTransaction transaction, IObjectCryptEngine explicitEngine)
    {
        if (sql == null) { throw new ArgumentNullException(nameof(sql)); }
        if (String.IsNullOrWhiteSpace(sql)) { throw new ArgumentException("The SQL statement cannot be empty or whitespace.", nameof(sql)); }
        SqliteCommand command = connection.CreateCommand();
        if (transaction != null) { command.Transaction = transaction; }
        command.CommandText = BindParameters(command, sql, param, connection, explicitEngine);
        return command;
    }

    private static string BindParameters(SqliteCommand command, string sql, object param, SqliteConnection connection, IObjectCryptEngine explicitEngine)
    {
        if (param == null) { return sql; }
        foreach (KeyValuePair<string, object> entry in EnumerateParameters(param))
        {
            string name = entry.Key.TrimStart('@', ':', '$');
            object value = entry.Value;
            if (!Regex.IsMatch(sql, $"@{Regex.Escape(name)}\\b", RegexOptions.IgnoreCase))
            {
                continue; //parameter not referenced in the SQL - skip it, matching Dapper behavior
            }
            if (value is EncryptedValue encryptedValue)
            {
                IObjectCryptEngine engine = RequireEngine(
                    ResolveEngine(connection, explicitEngine), $"Parameter '@{name}' is an EncryptedValue");
                string encrypted = engine.EncryptObject(encryptedValue.Value);
                command.Parameters.AddWithValue($"@{name}", (object)encrypted ?? DBNull.Value);
            }
            else if (value is IEnumerable list && !(value is string) && !(value is byte[]))
            {
                sql = ExpandListParameter(command, sql, name, list);
            }
            else
            {
                command.Parameters.AddWithValue($"@{name}", ToParameterValue(value));
            }
        }
        return sql;
    }

    private static IEnumerable<KeyValuePair<string, object>> EnumerateParameters(object param)
    {
        if (param is IDictionary<string, object> dictionary)
        {
            foreach (KeyValuePair<string, object> entry in dictionary) { yield return entry; }
            yield break;
        }
        foreach (PropertyInfo property in param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) { continue; }
            yield return new KeyValuePair<string, object>(property.Name, property.GetValue(param));
        }
    }

    private static string ExpandListParameter(SqliteCommand command, string sql, string name, IEnumerable list)
    {
        var parameterNames = new List<string>();
        int index = 0;
        foreach (object element in list)
        {
            string elementName = $"@{name}{++index}";
            parameterNames.Add(elementName);
            command.Parameters.AddWithValue(elementName, ToParameterValue(element));
        }
        //An empty list expands to (NULL), which matches no rows - the useful IN () semantics.
        string expansion = parameterNames.Count == 0 ? "(NULL)" : $"({String.Join(", ", parameterNames)})";
        return Regex.Replace(sql, $"@{Regex.Escape(name)}\\b", expansion, RegexOptions.IgnoreCase);
    }

    private static object ToParameterValue(object value)
    {
        if (value == null) { return DBNull.Value; }
        if (value is Enum) { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
        return value;
    }

    #endregion

    #region Materialization

    private static readonly ConcurrentDictionary<Type, Dictionary<string, MappedProperty>> PropertyMaps =
        new ConcurrentDictionary<Type, Dictionary<string, MappedProperty>>();

    private static readonly ConcurrentDictionary<Type, MethodInfo> DecryptMethods =
        new ConcurrentDictionary<Type, MethodInfo>();

    private static readonly MethodInfo OpenDecryptMethod =
        typeof(IObjectCryptEngine).GetMethod(nameof(IObjectCryptEngine.DecryptObject));

    private sealed class MappedProperty
    {
        public PropertyInfo Property;
        public bool IsEncrypted;
    }

    private static List<T> BufferRows<T>(SqliteDataReader reader, IObjectCryptEngine engine)
    {
        var rows = new List<T>();
        while (reader.Read()) { rows.Add(MaterializeRow<T>(reader, engine)); }
        return rows;
    }

    internal static T MaterializeRow<T>(SqliteDataReader reader, IObjectCryptEngine engine)
    {
        Type type = typeof(T);
        if (typeof(EncryptedTableItem).IsAssignableFrom(type))
        {
            return (T)MaterializeEncryptedTableItem(reader, type, engine);
        }
        if (IsSimpleType(type))
        {
            object converted = ConvertValue(reader.GetValue(0), type);
            return converted == null ? default : (T)converted;
        }
        return (T)MaterializePoco(reader, type, engine);
    }

    private static object MaterializeEncryptedTableItem(SqliteDataReader reader, Type type, IObjectCryptEngine engine)
    {
        int objectOrdinal = -1;
        int idOrdinal = -1;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string name = reader.GetName(i);
            if (String.Equals(name, "Encrypted_Object", StringComparison.OrdinalIgnoreCase)) { objectOrdinal = i; }
            else if (String.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)) { idOrdinal = i; }
        }
        if (objectOrdinal < 0)
        {
            throw new EncryptedTableException(
                $"Materializing '{type.Name}' requires the [Encrypted_Object] column in the result set - select it explicitly, or use SELECT *.");
        }
        IObjectCryptEngine requiredEngine = RequireEngine(engine, $"Result type '{type.Name}' is an encrypted table item");
        var item = (EncryptedTableItem)DecryptToType(requiredEngine, reader.GetString(objectOrdinal), type);
        if (idOrdinal >= 0 && !reader.IsDBNull(idOrdinal)) { item.Id = reader.GetInt64(idOrdinal); }
        item.SyncStatus = TableItemStatus.Unchanged;
        return item;
    }

    private static object MaterializePoco(SqliteDataReader reader, Type type, IObjectCryptEngine engine)
    {
        Dictionary<string, MappedProperty> map = PropertyMaps.GetOrAdd(type, BuildPropertyMap);
        object instance = Activator.CreateInstance(type);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (!map.TryGetValue(NormalizeName(reader.GetName(i)), out MappedProperty mapped)) { continue; }
            if (reader.IsDBNull(i)) { continue; }
            object value;
            if (mapped.IsEncrypted)
            {
                IObjectCryptEngine requiredEngine = RequireEngine(
                    engine, $"Property '{mapped.Property.Name}' of '{type.Name}' is marked [EncryptedColumn]");
                value = DecryptToType(requiredEngine, reader.GetString(i), mapped.Property.PropertyType);
            }
            else
            {
                value = ConvertValue(reader.GetValue(i), mapped.Property.PropertyType);
            }
            mapped.Property.SetValue(instance, value);
        }
        return instance;
    }

    private static dynamic MaterializeDynamicRow(SqliteDataReader reader)
    {
        IDictionary<string, object> row = new ExpandoObject();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }
        return row;
    }

    private static Dictionary<string, MappedProperty> BuildPropertyMap(Type type)
    {
        var map = new Dictionary<string, MappedProperty>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) { continue; }
            map[NormalizeName(property.Name)] = new MappedProperty
            {
                Property = property,
                IsEncrypted = property.GetCustomAttribute<EncryptedColumnAttribute>() != null
            };
        }
        return map;
    }

    private static string NormalizeName(string name)
        => name.Replace("_", "").ToLowerInvariant();

    private static object DecryptToType(IObjectCryptEngine engine, string encrypted, Type targetType)
    {
        MethodInfo method = DecryptMethods.GetOrAdd(targetType, t => OpenDecryptMethod.MakeGenericMethod(t));
        try
        {
            return method.Invoke(engine, new object[] { encrypted });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; //unreachable - Throw() above always throws
        }
    }

    private static T ConvertScalar<T>(object value)
        => value == null || value == DBNull.Value ? default : (T)ConvertValue(value, typeof(T));

    private static bool IsSimpleType(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsPrimitive || target.IsEnum || target == typeof(string) || target == typeof(decimal)
            || target == typeof(DateTime) || target == typeof(DateTimeOffset) || target == typeof(TimeSpan)
            || target == typeof(Guid) || target == typeof(byte[]) || target == typeof(object);
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (value == null || value == DBNull.Value) { return null; }
        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target == typeof(object) || target.IsInstanceOfType(value)) { return value; }
        if (target.IsEnum)
        {
            return value is string text
                ? Enum.Parse(target, text, ignoreCase: true)
                : Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        if (target == typeof(Guid))
        {
            return value is byte[] blob ? new Guid(blob) : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        if (target == typeof(DateTime)) { return Convert.ToDateTime(value, CultureInfo.InvariantCulture); }
        if (target == typeof(DateTimeOffset)) { return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
        if (target == typeof(TimeSpan)) { return TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
        if (target == typeof(char))
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text.Length > 0 ? text[0] : default(char);
        }
        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    #endregion
}
