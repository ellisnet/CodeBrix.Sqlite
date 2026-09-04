================================================================================
AGENT-README: CodeBrix.Sqlite
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Sqlite.ApacheLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Sqlite is a SQLite convenience library layered on top of
Microsoft.Data.Sqlite, for .NET 10 or later. It provides:

  - selective column/object encryption with a pluggable crypt engine (a
    production-ready AES-GCM engine is included);
  - the typed EncryptedTable<T> abstraction, with searchable encrypted data
    and HMAC blind-index equality search;
  - a Dapper-style CRUD mapper over SqliteConnection that is
    encryption-aware;
  - safe quiesce-and-backup orchestration for live databases (maintenance
    mode, WAL checkpoint, SQLite online backup, VACUUM INTO snapshots);
  - user_version schema-version helpers.

Encryption is entirely optional: with no crypt engine, the library is a
straightforward convenience layer over Microsoft.Data.Sqlite plus a mapper.

The library is the modern successor to two earlier open-source projects by
the same author: Portable.Data.Sqlite (origin of EncryptedTable<T>) and
SimpleAdo.Sqlite (origin of the encrypted-column API and the
maintenance-mode/backup concepts). The Dapper-style mapper is modeled on the
Dapper API surface. See THIRD-PARTY-NOTICES.txt in the package for the full
provenance record.


INSTALLATION
============
PackageId:  CodeBrix.Sqlite.ApacheLicenseForever

    dotnet add package CodeBrix.Sqlite.ApacheLicenseForever

IMPORTANT: the root namespace is CodeBrix.Sqlite, WITHOUT the
".ApacheLicenseForever" suffix - that suffix appears only in the NuGet
package id, to make the package's license obvious forever.

NuGet dependencies (pulled in automatically):
  - Microsoft.Data.Sqlite
  - SQLitePCLRaw.bundle_e_sqlite3

License: Apache-2.0 (SPDX: Apache-2.0)

Requirements: .NET 10 or later. No native prerequisites - the SQLite engine
itself ships inside the SQLitePCLRaw bundle, so the package works on Windows,
Linux and macOS with nothing installed on the machine.

DEPENDENCY NOTE (a durable rule, not a version to copy): the library pins the
SQLitePCLRaw bundle EXPLICITLY to the current 3.x native bundle rather than
accepting whichever bundle Microsoft.Data.Sqlite would otherwise bring in
transitively. The practical consequence for you: referencing CodeBrix.Sqlite
gives a SQLite dependency graph that
`dotnet list package --vulnerable --include-transitive` reports clean, and your
project needs no pin of its own. Do not add a downgrade pin of SQLitePCLRaw to
"align" versions - the rule is to keep the graph on the current 3.x bundle. One
release in the older 2.1.x line did carry a high-severity advisory (NU1903 /
GHSA-2m69-gcr7-jv3q), but the bundle Microsoft.Data.Sqlite resolves on its own
is no longer flagged, so a downgrade is a currency problem rather than an
advisory one.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Sqlite;                  // SqliteDatabase,
                                            //   SqliteDatabaseOptions,
                                            //   SqliteMapper (the Dapper-style
                                            //   extension methods),
                                            //   SqliteGridReader,
                                            //   EncryptedValue,
                                            //   EncryptedColumnAttribute
    using CodeBrix.Sqlite.Cryptography;     // IObjectCryptEngine,
                                            //   AesGcmCryptEngine,
                                            //   IBlindIndexProvider,
                                            //   IObjectSerializer,
                                            //   JsonObjectSerializer
    using CodeBrix.Sqlite.EncryptedTables;  // EncryptedTable<T>,
                                            //   EncryptedTableItem,
                                            //   TableItemStatus, the property
                                            //   attributes, TableSearch,
                                            //   TableSearchItem, TableColumn,
                                            //   TableIndex
    using CodeBrix.Sqlite.Extensions;       // SqliteCommand /
                                            //   SqliteDataReader extensions
    using CodeBrix.Sqlite.Enumerations;     // DbNullHandling
    using CodeBrix.Sqlite.Exceptions;       // CodeBrixSqliteException family
    using Microsoft.Data.Sqlite;            // SqliteConnection, SqliteCommand

The Dapper-style CRUD methods are extension methods on SqliteConnection
declared in the CodeBrix.Sqlite namespace: write `using CodeBrix.Sqlite;`
where you would have written `using Dapper;`.


QUICK START (no encryption)
===========================
The cryptEngine constructor argument defaults to null and everything below
works without one. The plain "convenience layer over Microsoft.Data.Sqlite"
case is five lines:

    using CodeBrix.Sqlite;

    using var db = new SqliteDatabase("app.db");
    db.SafeOpen();  // creates the file if missing; WAL + foreign keys on
    db.ExecuteNonQuery(
        "CREATE TABLE IF NOT EXISTS tickets (id INTEGER PRIMARY KEY, title TEXT);");

    //Dapper-style CRUD hangs off the Connection property - see below:
    db.Connection.Execute("INSERT INTO tickets (title) VALUES (@Title);",
        new { Title = "Investigate timeout" });
    List<Ticket> rows = db.Connection
        .Query<Ticket>("SELECT id, title FROM tickets ORDER BY id").ToList();

Add a crypt engine later - new SqliteDatabase(path, cryptEngine) - and the
encryption features light up without changing any of the above.


================================================================================

CORE API REFERENCE
==================

SqliteDatabase - THE ENTRY POINT (IDisposable)
----------------------------------------------
    SqliteDatabase(string databaseFilePath,
                   IObjectCryptEngine cryptEngine = null,
                   SqliteDatabaseOptions options = null)
        cryptEngine is OPTIONAL - omit it entirely if you do not need
        encryption (see QUICK START above).

Properties:
    string DatabaseFilePath { get; }
    SqliteConnection Connection { get; }
    IObjectCryptEngine CryptEngine { get; }
    IObjectSerializer Serializer { get; }
    SqliteDatabaseOptions Options { get; }
    System.Data.ConnectionState State { get; }
    bool IsInMaintenanceMode { get; }

Connection is THE BRIDGE TO THE MAPPER. The Dapper-style CRUD methods below
are extension methods on SqliteConnection, not members of this class, so
every mapper call is written database.Connection.Query<T>(...),
database.Connection.Execute(...), and so on. Those calls still honor the
maintenance-mode gate and pick up this database's crypt engine ambiently.
Raw ADO.NET work issued directly against the connection (a hand-built
command, BeginTransaction()) does NOT pass through the maintenance-mode gate;
prefer the methods on SqliteDatabase where an equivalent exists.

Opening and closing:
    void Open()
    Task OpenAsync(CancellationToken cancellationToken = default)
    void SafeOpen()
    Task SafeOpenAsync(CancellationToken cancellationToken = default)
    void Close()
        Open() assumes a CLOSED connection and throws if it is already open;
        SafeOpen() is the idempotent form, opening only when the connection is
        not already open. SafeOpen()/SafeOpenAsync() are what per-request or
        per-command code wants; reach for Open() only when you control the
        lifecycle and know the connection is closed. Opening applies the
        configured pragmas.

Commands:
    SqliteCommand CreateCommand(string commandText = null,
                                bool forMaintenance = false)
    int ExecuteNonQuery(string sql, bool forMaintenance = false)
    Task<int> ExecuteNonQueryAsync(string sql, bool forMaintenance = false,
                                   CancellationToken cancellationToken = default)
    object ExecuteScalar(string sql, bool forMaintenance = false)
    Task<object> ExecuteScalarAsync(string sql, bool forMaintenance = false,
                                    CancellationToken cancellationToken = default)

Schema version (SQLite's user_version):
    long GetSchemaVersion()
    Task<long> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    void SetSchemaVersion(long version)
    Task SetSchemaVersionAsync(long version,
                               CancellationToken cancellationToken = default)
        Set runs inside maintenance mode automatically; both preserve the
        open/closed state of the connection.

Maintenance mode:
    bool BeginMaintenanceMode()
    bool EndMaintenanceMode()
    bool IsInMaintenanceMode { get; }
        While in maintenance mode, normal operations throw
        DatabaseMaintenanceException; only forMaintenance operations run.

Backup and snapshot:
    void BackupToFile(string destinationFilePath)
    Task BackupToFileAsync(string destinationFilePath,
                           CancellationToken cancellationToken = default)
        Safe orchestration: quiesce (maintenance mode) -> PRAGMA
        wal_checkpoint(TRUNCATE) -> SQLite online backup -> resume.
        OVERWRITES an existing destination file.
    void SnapshotToFile(string destinationFilePath)
    Task SnapshotToFileAsync(string destinationFilePath,
                             CancellationToken cancellationToken = default)
        One-statement consistent snapshot via VACUUM INTO. The destination
        must NOT exist (throws IOException).

    void Dispose()


SqliteDatabaseOptions
---------------------
    bool UseWriteAheadLogging { get; set; }   // default true  (WAL journal)
    bool EnforceForeignKeys { get; set; }     // default true
    bool CreateIfMissing { get; set; }        // default true; false makes
                                              //   opening a missing file fail
    IObjectSerializer Serializer { get; set; }
        The serializer exposed as SqliteDatabase.Serializer for consumer code.
        NOTE that crypt engines carry their OWN serializer for
        encrypt/decrypt - see the AesGcmCryptEngine constructors.


CRYPTOGRAPHY
------------
    public interface IObjectCryptEngine : IDisposable
    {
        string EncryptObject(object valueToEncrypt);   // null in -> null out
        T DecryptObject<T>(string encryptedValue);
    }

    public interface IBlindIndexProvider
    {
        string ComputeBlindIndex(string value);        // null in -> null out
    }

    public interface IObjectSerializer
    {
        string Serialize(object value);
        T Deserialize<T>(string serialized);
    }

AesGcmCryptEngine : IObjectCryptEngine, IBlindIndexProvider (sealed)
    AesGcmCryptEngine(string passphrase,
                      byte[] salt = null,
                      IObjectSerializer serializer = null)
        Derives the AES key from the passphrase with PBKDF2 (SHA-256,
        100,000 iterations). A null salt uses a fixed library-default salt;
        pass an application-specific salt for isolation between applications
        that share a passphrase. Throws ArgumentNullException on a null
        passphrase and ArgumentException on an empty/whitespace one.
    AesGcmCryptEngine(byte[] key, IObjectSerializer serializer = null)
        Uses a raw 32-byte (256-bit) AES key. The array is copied. Throws
        ArgumentNullException on null and ArgumentException when the key is
        not exactly 32 bytes.
    IObjectSerializer Serializer { get; }
    string EncryptObject(object valueToEncrypt)
    T DecryptObject<T>(string encryptedValue)
    string ComputeBlindIndex(string value)
    void Dispose()

    Format: AES-GCM with a random 12-byte nonce per value; the stored string
    is Base64(nonce || tag || ciphertext). The blind index is HMAC-SHA256
    under a secondary key derived from the master key with HKDF, so it is
    deterministic but does not reveal the plaintext.

JsonObjectSerializer : IObjectSerializer
    JsonObjectSerializer()                                  // IncludeFields = true
    JsonObjectSerializer(System.Text.Json.JsonSerializerOptions options)
    string Serialize(object value)
    T Deserialize<T>(string serialized)


ENCRYPTED COLUMN EXTENSIONS
---------------------------
Namespace CodeBrix.Sqlite.Extensions.

SqliteCommandExtensions:
    static SqliteParameter AddEncryptedParameter(this SqliteCommand command,
        string parameterName, object value, IObjectCryptEngine cryptEngine)
    static T ExecuteDecrypt<T>(this SqliteCommand command,
        IObjectCryptEngine cryptEngine,
        DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    static Task<T> ExecuteDecryptAsync<T>(this SqliteCommand command,
        IObjectCryptEngine cryptEngine,
        DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException,
        CancellationToken cancellationToken = default)
    static long ExecuteReturnRowId(this SqliteCommand command)
        // INSERT + last_insert_rowid()
    static Task<long> ExecuteReturnRowIdAsync(this SqliteCommand command,
        CancellationToken cancellationToken = default)

SqliteDataReaderExtensions:
    static T GetDecrypted<T>(this SqliteDataReader reader, int ordinal,
        IObjectCryptEngine cryptEngine,
        DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    static T GetDecrypted<T>(this SqliteDataReader reader, string columnName,
        IObjectCryptEngine cryptEngine,
        DbNullHandling dbNullHandling = DbNullHandling.ThrowDbNullException)
    static bool TryDecrypt<T>(this SqliteDataReader reader, int ordinal,
        IObjectCryptEngine cryptEngine, out T value)
    static bool TryDecrypt<T>(this SqliteDataReader reader, string columnName,
        IObjectCryptEngine cryptEngine, out T value)

DbNullHandling (namespace CodeBrix.Sqlite.Enumerations):
    ThrowDbNullException  = 0   // default: throw DbNullValueException on NULL
    ReturnTypeDefaultValue = 1  // return default(T) on NULL


================================================================================

ENCRYPTED TABLES
================

EncryptedTableItem - the base class for stored objects
------------------------------------------------------
    public abstract class EncryptedTableItem
    {
        long Id { get; set; }                    // default -1 until written
        TableItemStatus SyncStatus { get; set; } // default New
    }

    public enum TableItemStatus
    {
        New = 0,            // not written to the table yet
        Unchanged = 1,      // matches the row it was read from or written to
        Modified = 2,       // has unwritten in-memory changes
        DeletePending = 3   // marked for deletion on the next write
    }

Derive from EncryptedTableItem and add public read/write properties.
Attribute-driven schema on those properties:
    [NotEncrypted]       a real plaintext column (combines with [ColumnName],
                         [NotNull] and [ColumnDefaultValue])
    [Searchable]         encrypted, but its value is also placed in the
                         in-memory searchable index (Encrypted_Searchable
                         column)
    [BlindIndexed]       encrypted plus a deterministic HMAC blind-index
                         column (BlindIndex_<Prop>, backed by a real SQLite
                         index) for exact-equality search; requires a crypt
                         engine implementing IBlindIndexProvider
    [ColumnName("X")]    override the column name for a plaintext column
    [NotNull]            NOT NULL on a plaintext column
    [ColumnDefaultValue("v")]  DEFAULT on a plaintext column
    untagged             encrypted, not searchable

    ColumnNameAttribute exposes string Name { get; };
    ColumnDefaultValueAttribute exposes string Value { get; }.

Generated table shape:
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    <the plaintext columns>,
    BlindIndex_<Prop> TEXT ... (one per [BlindIndexed] property),
    Encrypted_Searchable TEXT,
    Encrypted_Object TEXT      (the whole serialized + encrypted item)

Note that [NotEncrypted] plaintext properties participate in the searchable
index too - a TableSearch can match on them without any [Searchable] marker.


EncryptedTable<T> (IDisposable) where T : EncryptedTableItem, new()
------------------------------------------------------------------
CONSTRUCTORS:
    EncryptedTable(SqliteDatabase database,
                   bool checkDbTable = true,
                   string tableName = null)
        Uses the crypt engine already attached to the database.
    EncryptedTable(IObjectCryptEngine cryptEngine,
                   SqliteDatabase database,
                   bool checkDbTable = true,
                   string tableName = null)
        Uses an explicit crypt engine.

    checkDbTable (default true) verifies the SQLite table exists, creating it
    and its blind indexes when necessary. tableName defaults to the name of
    the item type; dots are replaced with underscores and the result must be
    a valid identifier (letters, digits, underscore, starting with a letter).

    Throws ArgumentNullException when database is null, and
    EncryptedTableException when no crypt engine is available, the table name
    is invalid, the item type's attributes are contradictory, or the item type
    has [BlindIndexed] properties while the crypt engine does not implement
    IBlindIndexProvider.

PROPERTIES:
    string TableName { get; }
    SqliteDatabase Database { get; }
    List<T> TempItems { get; }               // the write-behind cache
    TableIndex FullTableIndex { get; }       // a CLONE of the in-memory index;
                                             //   reading it builds/rebuilds
                                             //   the index when missing or
                                             //   expired
    int IndexLifetimeSeconds { get; set; }   // TTL of the in-memory index;
                                             //   0 rebuilds every time,
                                             //   negatives clamp to 0
    bool WriteChangesOnDispose { get; set; } // default true
    Dictionary<string, TableColumn> TableColumns { get; }  // keyed by column
                                                           //   name

WRITE-BEHIND CACHE:
    long AddItem(T item, bool immediateWriteToTable = false)
        Returns the temporary (negative) id, or the real row id when written
        immediately. Throws EncryptedTableException if the item was already
        written or is already tracked.
    void UpdateItem(T item)
    void RemoveItem(long itemId, bool immediateWriteToTable = false)
    int WriteItemChanges()
    Task<int> WriteItemChangesAsync(CancellationToken cancellationToken = default)
        Persists the pending adds/updates/removes and assigns real ids.
    void CheckDbTable()
    void Dispose()                            // flushes when
                                              //   WriteChangesOnDispose

READS AND SEARCHES:
    T GetItem(long itemId, bool exceptionOnMissingItem = false)
    Task<T> GetItemAsync(long itemId, bool exceptionOnMissingItem = false,
                         CancellationToken cancellationToken = default)
    List<T> GetItems(TableSearch search, bool writeChangesFirst = true)
    Task<List<T>> GetItemsAsync(TableSearch search,
                                bool writeChangesFirst = true,
                                CancellationToken cancellationToken = default)
    List<T> FindByBlindIndex(string propertyName, string value,
                             bool writeChangesFirst = true)
    Task<List<T>> FindByBlindIndexAsync(string propertyName, string value,
                                bool writeChangesFirst = true,
                                CancellationToken cancellationToken = default)

    GetItems evaluates the search against the TTL-cached in-memory index
    (built by decrypting ONLY the Encrypted_Searchable column) and then
    decrypts the full object of matching items only. It throws
    ArgumentNullException on a null search and EncryptedTableException when a
    criterion names a property that is not searchable.

    FindByBlindIndex runs a SQL-indexed equality lookup on the HMAC column and
    never decrypts non-matching rows. Matching is exact and CASE-SENSITIVE. It
    throws ArgumentNullException on a null propertyName/value and
    EncryptedTableException when the property is not [BlindIndexed].

SEARCHABLE-INDEX MANAGEMENT:
    int BuildFullTableIndex()                 // returns the number of rows
                                              //   indexed
    Task<int> BuildFullTableIndexAsync(CancellationToken cancellationToken = default)
    void DropFullTableIndex()
    bool CheckFullTableIndex(bool rebuildIfExpired = false)
                                              // true when a current index
                                              //   exists after the call


TableSearch and TableSearchItem - the search API
------------------------------------------------
    public enum TableSearchType
    {
        MatchAll = 0,   // logical AND across criteria (the default)
        MatchAny = 1    // logical OR across criteria
    }

    public enum SearchItemMatchType
    {
        IsEqualTo = 0,
        IsNotEqualTo = 1,
        Contains = 2,
        DoesNotContain = 3,
        StartsWith = 4,
        EndsWith = 5
    }

    public class TableSearchItem
    {
        TableSearchItem(string propertyName, string value,
                        SearchItemMatchType matchType
                            = SearchItemMatchType.IsEqualTo);
        string PropertyName { get; }
        string Value { get; }
        SearchItemMatchType MatchType { get; }
    }

    public class TableSearch
    {
        TableSearch();
        TableSearch(params TableSearchItem[] matchItems);
        TableSearchType SearchType { get; set; }     // default MatchAll
        List<TableSearchItem> MatchItems { get; }    // read-only property,
                                                     //   mutable list
        bool CaseSensitive { get; set; }             // default false
        bool TrimValues { get; set; }                // default true
    }

Build a search either by passing criteria to the constructor or by adding to
MatchItems:

    var search = new TableSearch(
        new TableSearchItem(nameof(ContactItem.Category), "Pioneers"),
        new TableSearchItem(nameof(ContactItem.FullName), "Ada",
                            SearchItemMatchType.StartsWith))
    {
        SearchType = TableSearchType.MatchAll,
        CaseSensitive = false,
        TrimValues = true
    };

    List<ContactItem> found = table.GetItems(search);


TableColumn and TableIndex - the supporting types
-------------------------------------------------
    public class TableColumn
    {
        string ColumnName { get; set; }
        string PropertyName { get; set; }
        string DataType { get; set; }      // the SQLite type: TEXT, INTEGER,
                                           //   REAL, BLOB
        bool IsNotNull { get; set; }
        string DefaultValue { get; set; }
        static bool IsValidIdentifier(string name);
    }

    public class TableIndex
    {
        DateTime CreatedUtc { get; set; }              // default UtcNow
        int LifetimeSeconds { get; set; }              // default 600
        Dictionary<long, Dictionary<string, string>> Items { get; }
                                                       // item id -> property
                                                       //   name -> value
        bool IsExpired { get; }
        TableIndex Clone();
    }

EncryptedTable<T>.FullTableIndex hands back a Clone(), so mutating what you
get does not corrupt the table's cached index.


================================================================================

DAPPER-STYLE CRUD (SqliteMapper)
================================
Extension methods on SqliteConnection, declared in the CodeBrix.Sqlite
namespace and modeled on the Dapper API surface ("using CodeBrix.Sqlite;"
instead of "using Dapper;"). Because they extend
SqliteConnection, call them through the Connection property of
SqliteDatabase: database.Connection.Query<T>(sql, param).

Every method has the same tail parameters:
    (this SqliteConnection connection, string sql, object param = null,
     SqliteTransaction transaction = null,
     IObjectCryptEngine cryptEngine = null)
and every async form appends
    (..., CancellationToken cancellationToken = default)

Synchronous:
    IEnumerable<T> Query<T>(...)
    IEnumerable<dynamic> Query(...)
    T QueryFirst<T>(...)
    T QueryFirstOrDefault<T>(...)
    T QuerySingle<T>(...)
    T QuerySingleOrDefault<T>(...)
    int Execute(...)
    T ExecuteScalar<T>(...)
    SqliteDataReader ExecuteReader(...)
    SqliteGridReader QueryMultiple(...)

Asynchronous (identical semantics):
    Task<IEnumerable<T>> QueryAsync<T>(...)
    Task<IEnumerable<dynamic>> QueryAsync(...)
    Task<T> QueryFirstAsync<T>(...)
    Task<T> QueryFirstOrDefaultAsync<T>(...)
    Task<T> QuerySingleAsync<T>(...)
    Task<T> QuerySingleOrDefaultAsync<T>(...)
    Task<int> ExecuteAsync(...)
    Task<T> ExecuteScalarAsync<T>(...)
    Task<SqliteDataReader> ExecuteReaderAsync(...)
    Task<SqliteGridReader> QueryMultipleAsync(...)

SqliteGridReader (sealed, IDisposable) - the result of QueryMultiple:
    bool IsConsumed { get; }
    IEnumerable<T> Read<T>()    // materializes the CURRENT result set
                                //   (buffered) and advances to the next
    void Dispose()              // disposes the reader and command, and
                                //   closes the connection if the
                                //   QueryMultiple call opened it
    Read<T>() throws InvalidOperationException once every result set has been
    consumed, and there is no public constructor - instances only come from
    QueryMultiple/QueryMultipleAsync.

PARAMETERS: anonymous objects, POCOs, or IDictionary<string, object>;
parameters not referenced in the SQL are skipped; IEnumerable parameter values
expand for IN clauses ("WHERE Id IN @ids"), and an empty list matches no rows.
Connections that were closed are opened for the call and closed after it
(readers and grid readers close on disposal).

COLUMN BINDING: result columns bind to writable public properties
case-insensitively AND ignoring underscores (both sides are normalized by
stripping '_' and lowercasing). A snake_case schema therefore maps onto
PascalCase properties with no aliases, no attributes and no configuration:
customer_tier -> CustomerTier, has_mitigation -> HasMitigation. Unlike stock
Dapper, this needs no MatchNamesWithUnderscores switch - do not go looking for
one, and do not write "SELECT customer_tier AS CustomerTier" aliases, which
are unnecessary here. Columns with no matching property are ignored, and a
column that is NULL leaves its property at the type's default value rather
than failing.

TYPE COERCION: values are converted with the usual .NET conversions
(Convert.ChangeType under InvariantCulture), with special handling for enums
(from a numeric value or a case-insensitive name), Guid (from a string or a
16-byte blob), DateTime, DateTimeOffset, TimeSpan and char. Because SQLite has
no boolean type, an INTEGER 0/1 column binds correctly to a bool property, and
a bool parameter stores as 0/1 - both directions work with no configuration.
ExecuteScalar<T>() applies the same conversions, so "SELECT
last_insert_rowid()" reads cleanly as ExecuteScalar<long>().

ENCRYPTION-AWARE: result types deriving from EncryptedTableItem are
materialized by decrypting the row's Encrypted_Object column (the result set
must include it - SELECT * works; Id is picked up when present); POCO
properties marked [EncryptedColumn] are decrypted on read; parameter values
wrapped in new EncryptedValue(obj) are encrypted on bind. The crypt engine
resolves from the optional cryptEngine argument, or ambiently from the
SqliteDatabase that owns the connection; mapper calls on a database in
maintenance mode throw DatabaseMaintenanceException. POCO materialization
needs a public parameterless constructor; multi-mapping (Query<T1,T2,...>) and
DynamicParameters are not included in this first iteration.

Supporting types (namespace CodeBrix.Sqlite):
    public sealed class EncryptedValue
    {
        EncryptedValue(object value);
        object Value { get; }
    }
    public class EncryptedColumnAttribute : Attribute { }


EXCEPTIONS
==========
All library exceptions derive from CodeBrixSqliteException, which derives from
System.Exception. Each type offers (string message) and
(string message, Exception innerException) constructors.

    CodeBrixSqliteException        the base type
      DatabaseMaintenanceException maintenance-mode gate violations
      ObjectCryptographyException  encrypt/decrypt/serialization failures
      EncryptedTableException      table mapping/name/search/missing-item
      DbNullValueException         NULL column under ThrowDbNullException

Argument validation uses the standard System exception types
(ArgumentNullException, ArgumentException, ObjectDisposedException,
InvalidOperationException, IOException).


================================================================================

COMPLETE EXAMPLES
=================

Example 1: End-to-end encrypted table - engine, table, search, backup
---------------------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using CodeBrix.Sqlite;
    using CodeBrix.Sqlite.Cryptography;
    using CodeBrix.Sqlite.EncryptedTables;

    //The stored item type: two plaintext columns, one searchable encrypted
    //  column, one searchable + blind-indexed column, one fully private column
    public class ContactItem : EncryptedTableItem
    {
        [NotEncrypted] public string Category { get; set; }

        [NotEncrypted, ColumnName("ContactAge")] public int Age { get; set; }

        [Searchable] public string FullName { get; set; }

        [Searchable, BlindIndexed] public string Email { get; set; }

        public string PrivateNotes { get; set; }
    }

    //1. the crypt engine (passphrase form; give it an app-specific salt)
    using var crypt = new AesGcmCryptEngine(
        "correct horse battery staple",
        salt: System.Text.Encoding.UTF8.GetBytes("my-app-v1-salt"));

    //2. the database, with the engine attached
    using var db = new SqliteDatabase("contacts.db", crypt);
    db.SafeOpen();

    //3. the table - created (with its blind index) on first construction
    using (var table = new EncryptedTable<ContactItem>(db))
    {
        //4. write-behind adds, then one flush
        table.AddItem(new ContactItem
        {
            Category = "Pioneers", Age = 36, FullName = "Ada Lovelace",
            Email = "ada@example.com", PrivateNotes = "analytical engine"
        });
        table.AddItem(new ContactItem
        {
            Category = "Pioneers", Age = 85, FullName = "Grace Hopper",
            Email = "grace@example.com", PrivateNotes = "compilers"
        });
        table.AddItem(new ContactItem
        {
            Category = "Modern", Age = 50, FullName = "Anita Borg",
            Email = "anita@example.com", PrivateNotes = "systers"
        });
        int written = table.WriteItemChanges();
        Console.WriteLine($"{written} rows written");

        //5a. indexed search over searchable + plaintext properties
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.Category), "Pioneers"),
            new TableSearchItem(nameof(ContactItem.FullName), "Ada",
                                SearchItemMatchType.StartsWith));

        List<ContactItem> matches = table.GetItems(search);
        foreach (ContactItem c in matches)
        {
            Console.WriteLine($"{c.Id}: {c.FullName} <{c.Email}>");
        }

        //5b. exact equality through the blind index (no full-table decrypt)
        List<ContactItem> byEmail = table.FindByBlindIndex(
            nameof(ContactItem.Email), "grace@example.com");
        Console.WriteLine($"blind-index hits: {byEmail.Count}");

        //5c. fetch by id
        ContactItem first = table.GetItem(matches[0].Id);
        Console.WriteLine(first.PrivateNotes);   // decrypted on read
    }   //Dispose flushes any pending changes (WriteChangesOnDispose)

    //6. safe backup of the live database, and a VACUUM INTO snapshot
    db.BackupToFile("contacts.backup.db");        // overwrites if present
    db.SnapshotToFile("contacts.snapshot.db");    // must NOT already exist


Example 2: Encrypting individual columns without EncryptedTable<T>
------------------------------------------------------------------
    using CodeBrix.Sqlite;
    using CodeBrix.Sqlite.Cryptography;
    using CodeBrix.Sqlite.Extensions;
    using Microsoft.Data.Sqlite;

    using var crypt = new AesGcmCryptEngine("passphrase");
    using var db = new SqliteDatabase("audit.db", crypt);
    db.SafeOpen();
    db.ExecuteNonQuery(
        "CREATE TABLE IF NOT EXISTS audit (id INTEGER PRIMARY KEY, " +
        "actor TEXT, secret TEXT);");

    using (SqliteCommand insert = db.CreateCommand(
        "INSERT INTO audit (actor, secret) VALUES (@actor, @secret);"))
    {
        insert.Parameters.AddWithValue("@actor", "svc-import");
        insert.AddEncryptedParameter("@secret", new { Token = "abc123" },
                                     crypt);
        long rowId = insert.ExecuteReturnRowId();
    }

    using (SqliteCommand read = db.CreateCommand(
        "SELECT secret FROM audit ORDER BY id DESC LIMIT 1;"))
    using (SqliteDataReader reader = read.ExecuteReader())
    {
        if (reader.Read())
        {
            var payload = reader.GetDecrypted<dynamic>("secret", crypt);
        }
    }


Example 3: Dapper-style CRUD, including a multi-result batch
------------------------------------------------------------
    using System.Collections.Generic;
    using System.Linq;
    using CodeBrix.Sqlite;

    public class Ticket
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string CustomerTier { get; set; }   // binds to customer_tier
        public bool HasMitigation { get; set; }    // binds to INTEGER 0/1
    }

    using var db = new SqliteDatabase("app.db");
    db.SafeOpen();

    db.Connection.Execute(
        "INSERT INTO tickets (title, customer_tier, has_mitigation) " +
        "VALUES (@Title, @Tier, @Mitigated);",
        new { Title = "Timeout", Tier = "gold", Mitigated = true });

    long newId = db.Connection.ExecuteScalar<long>(
        "SELECT last_insert_rowid();");

    var ids = new[] { 1L, 2L, 3L };
    List<Ticket> some = db.Connection
        .Query<Ticket>("SELECT * FROM tickets WHERE id IN @ids;",
                       new { ids })
        .ToList();

    using (SqliteGridReader grid = db.Connection.QueryMultiple(
        "SELECT * FROM tickets; SELECT COUNT(*) FROM tickets;"))
    {
        List<Ticket> all = grid.Read<Ticket>().ToList();
        long count = grid.Read<long>().First();
    }


Example 4: Schema versioning and a maintenance-mode migration
-------------------------------------------------------------
    using CodeBrix.Sqlite;

    using var db = new SqliteDatabase("app.db");
    db.SafeOpen();

    if (db.GetSchemaVersion() < 2)
    {
        db.BeginMaintenanceMode();
        try
        {
            //only forMaintenance operations run while quiesced
            db.ExecuteNonQuery(
                "ALTER TABLE tickets ADD COLUMN closed_on TEXT;",
                forMaintenance: true);
        }
        finally
        {
            db.EndMaintenanceMode();
        }

        db.SetSchemaVersion(2);   // runs inside maintenance mode itself
    }


================================================================================

MINIMUM VIABLE PROJECT
======================
A console application that creates an encrypted table, writes a row and reads
it back.

MyVault.csproj:
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Sqlite.ApacheLicenseForever" />
      </ItemGroup>
    </Project>

(Add the package with `dotnet add package
CodeBrix.Sqlite.ApacheLicenseForever` so the current version is written into
the csproj.)

Program.cs:
    using System;
    using CodeBrix.Sqlite;
    using CodeBrix.Sqlite.Cryptography;
    using CodeBrix.Sqlite.EncryptedTables;

    public class Note : EncryptedTableItem
    {
        [NotEncrypted] public string Folder { get; set; }
        [Searchable] public string Title { get; set; }
        public string Body { get; set; }        // encrypted, not searchable
    }

    public static class Program
    {
        public static int Main()
        {
            using var crypt = new AesGcmCryptEngine("demo passphrase");
            using var db = new SqliteDatabase("vault.db", crypt);
            db.SafeOpen();

            using var notes = new EncryptedTable<Note>(db);
            notes.AddItem(new Note
            {
                Folder = "inbox",
                Title = "Reset the staging key",
                Body = "rotate on Friday"
            });
            notes.WriteItemChanges();

            var search = new TableSearch(
                new TableSearchItem(nameof(Note.Folder), "inbox"));

            foreach (Note n in notes.GetItems(search))
            {
                Console.WriteLine($"{n.Id}: {n.Title} - {n.Body}");
            }

            return 0;
        }
    }


================================================================================

PERFORMANCE TIPS
================

1.  Use SafeOpen()/SafeOpenAsync() and keep ONE SqliteDatabase per database
    file for the lifetime of the work. Opening applies pragmas each time, and
    the mapper opens/closes a closed connection around every call.

2.  Batch encrypted-table writes. AddItem/UpdateItem/RemoveItem only touch the
    in-memory cache; a single WriteItemChanges() persists them all. Pass
    immediateWriteToTable: true only when you genuinely need the real id right
    away.

3.  Prefer [BlindIndexed] + FindByBlindIndex over TableSearch for
    exact-equality lookups on large tables: the blind index is a real SQLite
    index, so non-matching rows are never decrypted.

4.  Searchable-index builds decrypt the Encrypted_Searchable column of EVERY
    row - an O(n) scan. It is cached with a TTL (IndexLifetimeSeconds,
    default 600 seconds; 0 rebuilds on every use). Raise the TTL for
    read-heavy workloads, and call BuildFullTableIndex() once up front rather
    than letting the first search pay for it.

5.  Keep [Searchable] to the properties you actually search on. Every
    searchable property enlarges the Encrypted_Searchable payload that the
    index decrypts.

6.  Reuse one crypt engine instance. AesGcmCryptEngine derives its key once in
    the constructor (PBKDF2 with 100,000 iterations is deliberately slow) -
    constructing one per operation is the single most expensive mistake you
    can make with this library.

7.  For bulk inserts through the mapper, open the connection yourself with
    SafeOpen() and wrap the batch in a transaction from
    Connection.BeginTransaction(), passing it as the transaction argument.

8.  SnapshotToFile() (VACUUM INTO) writes a compacted copy in one statement
    and is usually faster and smaller than BackupToFile(); use BackupToFile()
    when you need to overwrite an existing destination or want the
    quiesce/checkpoint/online-backup sequence.

9.  Query<T>() buffers its rows; for very large result sets prefer
    ExecuteReader() and read incrementally.

10. Cache TableColumns / FullTableIndex results if you use them in a loop:
    both build a fresh object (FullTableIndex returns a Clone()) on every
    access.


================================================================================

COMMON PITFALLS TO AVOID
========================

- The package id and the namespace differ: package
  CodeBrix.Sqlite.ApacheLicenseForever, namespace CodeBrix.Sqlite.

- EncryptedTable<T>.GetItem() returns the TRACKED IN-MEMORY instance when an
  item with that id is in TempItems - not a fresh copy from the table. Clear
  or flush TempItems first if you need to verify what is on disk.

- GetItems() and FindByBlindIndex() write pending item changes to the table
  FIRST by default (writeChangesFirst: true) so searches see them; pass false
  to search only what is already persisted.

- An empty TableSearch matches EVERY item under MatchAll (all-of-nothing is
  true) and NO items under MatchAny.

- TableSearch is case-INSENSITIVE and trims values by default. Set
  CaseSensitive = true / TrimValues = false when that matters.

- Blind-index matching is exact and CASE-SENSITIVE regardless of the
  TableSearch settings (a blind index is a hash, not a comparison). For
  case-insensitive equality, normalize the value (for example
  ToLowerInvariant()) both when storing and when searching.

- A [BlindIndexed] property requires a crypt engine that implements
  IBlindIndexProvider; AesGcmCryptEngine does, a custom engine may not. The
  EncryptedTable<T> constructor throws EncryptedTableException if it does not.

- Searchable-index builds decrypt the Encrypted_Searchable column of EVERY row
  - an O(n) scan, cached with a TTL (IndexLifetimeSeconds, default 600s; 0 =
  rebuild every use). Fine at app-local sizes; use [BlindIndexed] for
  large-table equality lookups.

- Reserved column names on encrypted tables: Id, Encrypted_Searchable,
  Encrypted_Object, and the BlindIndex_* prefix. A [ColumnName] that collides
  throws EncryptedTableException at table construction.

- A new EncryptedTableItem has Id == -1 and SyncStatus == New until
  WriteItemChanges() assigns the real row id. Do not persist the temporary
  negative id anywhere.

- Transactions require an already-open connection (same as Dapper): call
  SafeOpen() before Connection.BeginTransaction().

- Raw ADO.NET work issued straight against Connection bypasses the
  maintenance-mode gate. Use the SqliteDatabase methods, or the mapper, where
  an equivalent exists.

- BackupToFile() OVERWRITES an existing destination file; SnapshotToFile()
  (VACUUM INTO) REFUSES one and throws IOException.

- POCO materialization in the Dapper-style methods needs a public
  parameterless constructor. C# 'required' members are FINE despite that:
  Query<T> and friends carry no "where T : new()" constraint (a type with
  required members could not satisfy one) and materialize reflectively via
  Activator.CreateInstance, which legitimately bypasses what is a compile-time
  contract. Do not strip 'required' from your models defensively - rows
  round-trip into it correctly.

- [NotNull] columns are not satisfied by [ColumnDefaultValue] on inserts (the
  INSERT lists every column explicitly), so give NOT NULL properties real
  values.

- A SqliteGridReader is single-pass: Read<T>() advances to the next result set
  and throws InvalidOperationException once IsConsumed is true. Read the sets
  in the order the SQL declares them.

- Losing the passphrase or key loses the data. There is no recovery path, no
  key escrow and no key-rotation helper - re-encrypting means reading every
  item with the old engine and writing it with the new one.

- A custom salt is part of the key: changing the salt changes the derived key
  and makes existing rows undecryptable. Choose one per application and keep
  it.


================================================================================

WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this library for:

  - Full-database (page-level) encryption. This is selective
    column/object encryption on top of a normal SQLite file; the file itself,
    its schema, its table and column names, and any plaintext columns are
    readable by any SQLite tool. It is not SQLCipher.
  - Range, ordering or LIKE queries over encrypted data in SQL. Encrypted
    columns are opaque to SQLite; searching happens either through the
    in-memory searchable index (which decrypts a projection of every row) or
    through blind-index EXACT equality.
  - Key management: no key derivation policy beyond PBKDF2/HKDF as described,
    no key storage, no rotation, no escrow.
  - Object-relational mapping. There is no change tracking outside
    EncryptedTable<T>'s write-behind cache, no LINQ provider, no lazy loading,
    no relationships and no migrations engine (schema versioning is just
    user_version read/write).
  - Multi-mapping (Query<T1, T2, ...>) or Dapper's DynamicParameters - not
    included in this first iteration of the mapper.
  - Database engines other than SQLite, or SQLite through a provider other
    than Microsoft.Data.Sqlite.
  - Cross-process coordination. Maintenance mode gates operations issued
    through THIS SqliteDatabase instance; another process holding the same
    file is unaffected.
  - Backup scheduling, retention or compression. BackupToFile /
    SnapshotToFile produce a file; what happens to it afterwards is your job.

CodeBrix.Sqlite IS for: application-local SQLite databases that need some
values encrypted at rest, want to search them without decrypting the whole
table, and need to be backed up safely while running.


================================================================================

WORKING EXAMPLES ON GITHUB
==========================
The test project is the executable documentation for this package. Browse it
at:

    https://github.com/ellisnet/CodeBrix.Sqlite/tree/main/tests/CodeBrix.Sqlite.Tests

Feature-to-test-file map:

  SqliteDatabase: open/close, pragmas, maintenance mode, schema version,
  backup and snapshot:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/SqliteDatabaseTests.cs

  Backup and restore scenarios end to end:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/BackupRestoreScenarios.cs
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/BackupZipRoundTrip.cs

  AES-GCM crypt engine (both constructor forms, blind index, round trips):
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/AesGcmCryptEngineTests.cs

  JSON object serializer:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/JsonObjectSerializerTests.cs

  EncryptedTable<T>: construction, write-behind cache, reads:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/EncryptedTableTests.cs

  Item property attributes and generated schema:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/EncryptedTableAttributeScenarios.cs

  TableSearch / TableSearchItem behavior against real tables:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/EncryptedTableSearchScenarios.cs
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/TableSearchTests.cs
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/TableSearchItemTests.cs

  Blind-index equality search:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/BlindIndexScenarios.cs

  TableColumn and TableIndex:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/TableColumnTests.cs
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/TableIndexTests.cs

  Dapper-style mapper: binding, coercion, parameters, QueryMultiple:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/SqliteMapperTests.cs

  Mapper + encryption ([EncryptedColumn], EncryptedValue, EncryptedTableItem
  results):
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/DapperStyleEncryptionScenarios.cs

  Encrypted-column command and reader extensions:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/SqliteCommandExtensionsTests.cs
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/SqliteDataReaderExtensionsTests.cs

  Exception types:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/CodeBrixSqliteExceptionTests.cs

  Volume and concurrency behavior:
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/VolumeAndConcurrencyScenarios.cs

  The sample item types used across the tests (a good template for your own
  EncryptedTableItem):
    https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/tests/CodeBrix.Sqlite.Tests/SampleItems.cs

To read a file's source directly, fetch the raw URL:
    https://raw.githubusercontent.com/ellisnet/CodeBrix.Sqlite/main/tests/CodeBrix.Sqlite.Tests/EncryptedTableTests.cs


================================================================================

QUICK REFERENCE CARD
====================

--- Install ---
dotnet add package CodeBrix.Sqlite.ApacheLicenseForever

--- Namespaces ---
using CodeBrix.Sqlite;                  // SqliteDatabase + mapper extensions
using CodeBrix.Sqlite.Cryptography;     // AesGcmCryptEngine, interfaces
using CodeBrix.Sqlite.EncryptedTables;  // EncryptedTable<T>, attributes,
                                        //   TableSearch
using CodeBrix.Sqlite.Extensions;       // encrypted command/reader helpers
using CodeBrix.Sqlite.Enumerations;     // DbNullHandling
using CodeBrix.Sqlite.Exceptions;       // exception types

--- Database ---
Create:             var db = new SqliteDatabase("app.db", cryptEngine)
Open (idempotent):  db.SafeOpen()   /   await db.SafeOpenAsync(ct)
Raw SQL:            db.ExecuteNonQuery(sql)  /  db.ExecuteScalar(sql)
Command:            db.CreateCommand(sql)
Schema version:     db.GetSchemaVersion()  /  db.SetSchemaVersion(2)
Maintenance:        db.BeginMaintenanceMode() ... db.EndMaintenanceMode()
Backup (overwrite): db.BackupToFile("app.backup.db")
Snapshot (no clobber): db.SnapshotToFile("app.snap.db")

--- Options ---
new SqliteDatabaseOptions { UseWriteAheadLogging = true,
                            EnforceForeignKeys = true,
                            CreateIfMissing = true,
                            Serializer = mySerializer }

--- Crypt engines ---
Passphrase:         new AesGcmCryptEngine("pass", salt, serializer)
Raw 32-byte key:    new AesGcmCryptEngine(keyBytes, serializer)
Encrypt/decrypt:    crypt.EncryptObject(obj) / crypt.DecryptObject<T>(s)
Blind index:        crypt.ComputeBlindIndex("value")

--- Item attributes ---
[NotEncrypted]                plaintext column
[NotEncrypted, ColumnName("X")]  renamed plaintext column
[NotEncrypted, NotNull, ColumnDefaultValue("g")]
[Searchable]                  encrypted + in the searchable index
[Searchable, BlindIndexed]    + exact-equality SQL index
(no attribute)                encrypted, not searchable

--- Encrypted table ---
Create:             new EncryptedTable<T>(db)
                    new EncryptedTable<T>(crypt, db, checkDbTable, tableName)
Add/update/remove:  table.AddItem(item) / UpdateItem(item) / RemoveItem(id)
Flush:              table.WriteItemChanges()   (also on Dispose by default)
By id:              table.GetItem(id)
Search:             table.GetItems(new TableSearch(new TableSearchItem(
                        nameof(T.Prop), "value", SearchItemMatchType.Contains)))
Blind index:        table.FindByBlindIndex(nameof(T.Email), "a@b.c")
Index control:      table.BuildFullTableIndex() / DropFullTableIndex()
                    table.CheckFullTableIndex(rebuildIfExpired: true)
                    table.IndexLifetimeSeconds = 600

--- Search options ---
new TableSearch(items) { SearchType = TableSearchType.MatchAny,
                         CaseSensitive = false, TrimValues = true }
Match types: IsEqualTo, IsNotEqualTo, Contains, DoesNotContain,
             StartsWith, EndsWith

--- Dapper-style CRUD (through db.Connection) ---
Query:              db.Connection.Query<T>(sql, param)
First/Single:       QueryFirst<T> / QueryFirstOrDefault<T> /
                    QuerySingle<T> / QuerySingleOrDefault<T>
Execute:            db.Connection.Execute(sql, param)
Scalar:             db.Connection.ExecuteScalar<long>("SELECT last_insert_rowid();")
Reader:             db.Connection.ExecuteReader(sql, param)
Multi-result:       using var grid = db.Connection.QueryMultiple(sql);
                    grid.Read<T>(); grid.Read<long>();
Async:              every method has a ...Async(..., cancellationToken) form
IN clause:          "WHERE Id IN @ids", new { ids = new[] { 1L, 2L } }
Encrypt a param:    new EncryptedValue(obj)
Decrypt a property: [EncryptedColumn] on the POCO property

--- Encrypted columns by hand ---
cmd.AddEncryptedParameter("@p", value, crypt)
cmd.ExecuteDecrypt<T>(crypt, DbNullHandling.ReturnTypeDefaultValue)
cmd.ExecuteReturnRowId()
reader.GetDecrypted<T>("column", crypt)
reader.TryDecrypt<T>("column", crypt, out var value)

--- Exceptions ---
CodeBrixSqliteException
  DatabaseMaintenanceException / ObjectCryptographyException /
  EncryptedTableException / DbNullValueException

Target: .NET 10 or later
License: Apache-2.0


================================================================================
END OF AGENT-README
