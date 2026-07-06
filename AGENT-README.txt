========================================================================
                    AGENT-README: CodeBrix.Sqlite
             A Comprehensive Guide for AI Coding Agents
========================================================================

OVERVIEW
------------------------------------------------------------------------
CodeBrix.Sqlite is a SQLite convenience library layered on top of
Microsoft.Data.Sqlite. It provides selective column/object encryption
with a pluggable crypt engine (a production-ready AES-GCM engine is
included), the typed EncryptedTable<T> abstraction with searchable
encrypted data and HMAC blind-index equality search, safe
quiesce-and-backup orchestration for live databases (maintenance mode,
WAL checkpoint, SQLite online backup, VACUUM INTO snapshots), and
user_version schema-version helpers.

The library is the modern successor to two earlier open-source projects
by the same author: Portable.Data.Sqlite (origin of EncryptedTable<T>)
and SimpleAdo.Sqlite (origin of the encrypted-column API and the
maintenance-mode/backup concepts). See THIRD-PARTY-NOTICES.txt for the
full provenance record.

INSTALLATION
------------------------------------------------------------------------
NuGet package:  CodeBrix.Sqlite.ApacheLicenseForever

    dotnet add package CodeBrix.Sqlite.ApacheLicenseForever

IMPORTANT: The root namespace is CodeBrix.Sqlite (WITHOUT the
".ApacheLicenseForever" suffix - that suffix appears only in the NuGet
package id, to make the package's license obvious forever).

Target framework: .NET 10.0 or higher. The library's only dependency is
Microsoft.Data.Sqlite (which bundles the native SQLite engine via
SQLitePCLRaw).

KEY NAMESPACE
------------------------------------------------------------------------
    using CodeBrix.Sqlite;                  // SqliteDatabase, SqliteDatabaseOptions
    using CodeBrix.Sqlite.Cryptography;     // IObjectCryptEngine, AesGcmCryptEngine,
                                            //   IBlindIndexProvider, IObjectSerializer,
                                            //   JsonObjectSerializer
    using CodeBrix.Sqlite.EncryptedTables;  // EncryptedTable<T>, EncryptedTableItem,
                                            //   attributes, TableSearch, TableIndex
    using CodeBrix.Sqlite.Extensions;       // SqliteCommand/SqliteDataReader extensions
    using CodeBrix.Sqlite.Enumerations;     // DbNullHandling
    using CodeBrix.Sqlite.Exceptions;       // CodeBrixSqliteException family

CORE API REFERENCE
------------------------------------------------------------------------
SqliteDatabase (entry point; IDisposable)
  - new SqliteDatabase(path, cryptEngine = null, options = null)
  - Open() / OpenAsync() / SafeOpen() / SafeOpenAsync() / Close()
      Opening applies the configured pragmas: WAL journal mode and
      foreign-key enforcement are ON by default (SqliteDatabaseOptions).
      SqliteDatabaseOptions also has: CreateIfMissing (default true - the
      database file is created if absent; false makes opening a missing
      file fail) and Serializer (the IObjectSerializer exposed via
      SqliteDatabase.Serializer for consumer code; note that crypt engines
      carry their OWN serializer for encrypt/decrypt - see the
      AesGcmCryptEngine constructors).
  - CreateCommand(sql = null, forMaintenance = false) -> SqliteCommand
  - ExecuteNonQuery(sql, forMaintenance = false) (+Async)
  - ExecuteScalar(sql, forMaintenance = false) (+Async)
  - GetSchemaVersion() / SetSchemaVersion(long) (+Async)
      Read/write SQLite's user_version; Set runs inside maintenance
      mode automatically; both preserve the open/closed state.
  - BeginMaintenanceMode() / EndMaintenanceMode() / IsInMaintenanceMode
      While in maintenance mode, normal operations throw
      DatabaseMaintenanceException; only forMaintenance operations run.
  - BackupToFile(path) (+Async)
      Safe orchestration: quiesce (maintenance mode) -> PRAGMA
      wal_checkpoint(TRUNCATE) -> SQLite online backup -> resume.
      Overwrites an existing destination file.
  - SnapshotToFile(path) (+Async)
      One-statement consistent snapshot via VACUUM INTO. Destination
      must not exist (throws IOException).

Cryptography
  - IObjectCryptEngine: EncryptObject(object) -> string,
    DecryptObject<T>(string) -> T. Implementations serialize the object
    (IObjectSerializer) then encrypt.
  - AesGcmCryptEngine: AES-GCM, random 12-byte nonce per value, stored
    string is Base64(nonce || tag || ciphertext). Key from a passphrase
    via PBKDF2 (SHA-256, 100k iterations; optional custom salt) or a raw
    32-byte key. Also implements IBlindIndexProvider via HMAC-SHA256
    with an HKDF-derived secondary key.
  - JsonObjectSerializer: System.Text.Json with IncludeFields = true.

Encrypted column extensions (CodeBrix.Sqlite.Extensions)
  - command.AddEncryptedParameter(name, value, cryptEngine)
  - command.ExecuteDecrypt<T>(cryptEngine, dbNullHandling) (+Async)
  - command.ExecuteReturnRowId() (+Async)   // INSERT + last_insert_rowid()
  - reader.GetDecrypted<T>(ordinalOrName, cryptEngine, dbNullHandling)
  - reader.TryDecrypt<T>(ordinalOrName, cryptEngine, out value)
  DbNullHandling.ThrowDbNullException (default) throws
  DbNullValueException on NULL; ReturnTypeDefaultValue returns default.

EncryptedTable<T> (T : EncryptedTableItem, new())
  Attribute-driven schema on the item type's public read/write props:
  - [NotEncrypted]       real plaintext column (supports [ColumnName],
                         [NotNull], [ColumnDefaultValue])
  - [Searchable]         encrypted, but included in the in-memory
                         searchable index (Encrypted_Searchable column)
  - [BlindIndexed]       encrypted + deterministic HMAC blind-index
                         column (BlindIndex_<Prop>, with a real SQLite
                         index) for exact-equality search; requires a
                         crypt engine implementing IBlindIndexProvider
  - untagged             encrypted, not searchable
  Generated table: Id INTEGER PRIMARY KEY AUTOINCREMENT, the plaintext
  columns, the BlindIndex_* columns, Encrypted_Searchable TEXT,
  Encrypted_Object TEXT (the whole serialized+encrypted item).

  Write-behind cache: AddItem(), UpdateItem(), RemoveItem() accumulate
  in TempItems with TableItemStatus sync states (New items get
  temporary negative ids); WriteItemChanges() (+Async) persists and
  assigns real ids; WriteChangesOnDispose (default true) flushes on
  Dispose().

  Reads/searches: GetItem(id) (+Async), GetItems(TableSearch) (+Async)
  evaluated against the TTL-cached in-memory index (built by decrypting
  ONLY the Encrypted_Searchable column), FindByBlindIndex(prop, value)
  (+Async) via the SQL-indexed HMAC column, BuildFullTableIndex() /
  DropFullTableIndex() / CheckFullTableIndex() / IndexLifetimeSeconds.

Dapper-style CRUD (SqliteMapper - namespace CodeBrix.Sqlite)
  Extension methods on SqliteConnection modeled on the Dapper 2.1.79 API
  surface ("using CodeBrix.Sqlite;" instead of "using Dapper;"):
  - Query<T>() / Query() dynamic / QueryFirst<T>() / QueryFirstOrDefault<T>()
    / QuerySingle<T>() / QuerySingleOrDefault<T>()
  - Execute() / ExecuteScalar<T>() / ExecuteReader() / QueryMultiple()
    (SqliteGridReader.Read<T>() per result set)
  - All of the above as ...Async(cancellationToken) forms.
  Parameters: anonymous objects, POCOs, or IDictionary<string, object>;
  parameters not referenced in the SQL are skipped; IEnumerable parameter
  values expand for IN clauses ("WHERE Id IN @ids"); an empty list matches
  no rows. Connections that were closed are opened for the call and closed
  after it (readers/grids close on disposal).
  ENCRYPTION-AWARE: result types deriving from EncryptedTableItem are
  materialized by decrypting the row's Encrypted_Object column (the result
  set must include it - SELECT * works; Id is picked up when present);
  POCO properties marked [EncryptedColumn] are decrypted on read; parameter
  values wrapped in new EncryptedValue(obj) are encrypted on bind. The crypt
  engine resolves from the optional cryptEngine argument, or ambiently from
  the SqliteDatabase that owns the connection; mapper calls on a database in
  maintenance mode throw DatabaseMaintenanceException. POCO materialization
  needs a public parameterless constructor; multi-mapping (Query<T1,T2,...>)
  and DynamicParameters are not included in this first iteration.

Exceptions (all derive from CodeBrixSqliteException)
  - DatabaseMaintenanceException: maintenance-mode gate violations
  - ObjectCryptographyException: encrypt/decrypt/serialization failures
  - EncryptedTableException: table mapping/name/search/missing-item
  - DbNullValueException: NULL column under ThrowDbNullException
  Argument validation uses the standard System exception types
  (ArgumentNullException, ArgumentException, etc.).

COMMON PITFALLS
------------------------------------------------------------------------
- EncryptedTable<T>.GetItem() returns the TRACKED IN-MEMORY instance when
  an item with that id is in TempItems - not a fresh copy from the table.
  Clear or flush TempItems first if you need to verify what is on disk.
- GetItems() and FindByBlindIndex() write pending item changes to the
  table FIRST by default (writeChangesFirst: true) so searches see them;
  pass false to search only what is already persisted.
- An empty TableSearch matches EVERY item under MatchAll (all-of-nothing
  is true) and NO items under MatchAny.
- Blind-index matching is exact and CASE-SENSITIVE. For case-insensitive
  equality, normalize the value (e.g. ToLowerInvariant()) both when
  storing and when searching.
- Searchable-index builds decrypt the Encrypted_Searchable column of
  EVERY row - an O(n) scan, cached with a TTL (IndexLifetimeSeconds,
  default 600s; 0 = rebuild every use). Fine at app-local sizes; use
  [BlindIndexed] for large-table equality lookups.
- Reserved column names on encrypted tables: Id, Encrypted_Searchable,
  Encrypted_Object, and the BlindIndex_* prefix. A [ColumnName] that
  collides throws EncryptedTableException at table construction.
- Transactions require an already-open connection (same as Dapper):
  call SafeOpen() before Connection.BeginTransaction().
- BackupToFile() OVERWRITES an existing destination file;
  SnapshotToFile() (VACUUM INTO) REFUSES one and throws IOException.
- POCO materialization in the Dapper-style methods needs a public
  parameterless constructor; [NotNull] columns are not satisfied by
  [ColumnDefaultValue] on inserts (the INSERT lists every column
  explicitly), so give NOT NULL properties real values.
- Two builds in the same UTC minute produce the SAME package version
  (date-stamped versioning) - do not publish two packages from one minute.

CODING CONVENTIONS (CodeBrix family)
------------------------------------------------------------------------
- Nullable reference types are OFF: never use '?' on reference types
  (string?, MyClass?) and never use the null-forgiveness '!' operator.
  Value-type nullables (int?, DateOnly?, MyEnum?) are fine.
- No <ImplicitUsings>, no global usings; every file lists its own
  using directives, System.* first, alphabetical within groups.
- File-scoped namespaces only (namespace X;), never block-scoped.
- Files ported from an upstream project keep the upstream copyright
  header and carry a "//was previously: <upstream-ns>;" comment on the
  namespace line. Never fabricate headers on new files.
- <GenerateDocumentationFile> is ON: every public (and protected on
  unsealed types) member carries an XML doc comment. Fix CS1591 at the
  source; never suppress warnings project-wide (<NoWarn> forbidden).
- Tests: xUnit v3 + SilverAssertions fluent asserts (x.Should().Be(y)).
  Test files are named <ClassUnderTest>Tests.cs; method names are
  MemberName_snake_case_description or pure snake_case; multi-statement
  tests carry //Arrange //Act //Assert comments; single-statement tests
  are expression-bodied. Tests pass TestContext.Current.CancellationToken
  to every cancellable call.
- The library project carries the canonical date-stamped version block;
  do not replace it with a literal <Version>.

ARCHITECTURE
------------------------------------------------------------------------
src/CodeBrix.Sqlite/
  (root)             SqliteDatabase (entry point), SqliteDatabaseOptions,
                     SqliteMapper (+ .Async partial; Dapper-style CRUD),
                     SqliteGridReader, EncryptedValue,
                     EncryptedColumnAttribute
  Cryptography/      IObjectCryptEngine, IBlindIndexProvider,
                     IObjectSerializer, JsonObjectSerializer,
                     AesGcmCryptEngine
  EncryptedTables/   EncryptedTable<T>, EncryptedTableItem (+
                     TableItemStatus), attribute set, TableColumn,
                     TableIndex, TableSearch(+Type), TableSearchItem
                     (ported from Portable.Data.Sqlite - see
                     THIRD-PARTY-NOTICES.txt)
  Extensions/        SqliteCommandExtensions, SqliteDataReaderExtensions
  Enumerations/      DbNullHandling
  Exceptions/        CodeBrixSqliteException + derived exception types
  InternalsVisibleTo.cs  grants internals access to CodeBrix.Sqlite.Tests

TESTING
------------------------------------------------------------------------
tests/CodeBrix.Sqlite.Tests/ is an xUnit v3 project using
SilverAssertions and coverlet.collector. It also references
CodeBrix.Compression.MitLicenseForever to exercise the full
backup -> zip -> unzip -> restore -> read round trip.

Run everything from the repo root:

    dotnet restore CodeBrix.Sqlite.slnx
    dotnet build   CodeBrix.Sqlite.slnx
    dotnet test    CodeBrix.Sqlite.slnx

Tests create their SQLite database files in per-test temporary folders
(see the TempFolder helper) and clear the Microsoft.Data.Sqlite
connection pools on cleanup so the files can be deleted. No network or
special environment is required.
========================================================================
