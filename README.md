# CodeBrix.Sqlite

A fully managed, cross-platform SQLite convenience library for .NET, layered on top of `Microsoft.Data.Sqlite`. At its simplest it is a convenience layer: modern pragma defaults, a Dapper-style mapper, and safe backups. Beyond that it provides selective column and object encryption with a pluggable crypt engine (including a ready-to-use AES-GCM engine), the typed `EncryptedTable<T>` abstraction with searchable encrypted data and HMAC blind-index equality search, safe quiesce-and-backup orchestration for live databases, and database schema-version helpers. **The encryption features are entirely optional** — see the plain sample below.
CodeBrix.Sqlite depends only on `Microsoft.Data.Sqlite` and its own version pin of that package's `SQLitePCLRaw` native bundle, and is provided as a .NET 10 library and associated `CodeBrix.Sqlite.ApacheLicenseForever` NuGet package.

CodeBrix.Sqlite supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Sqlite.ApacheLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Sqlite`:

* NuGet package ID: `CodeBrix.Sqlite.ApacheLicenseForever`
* Assembly and root namespace: `CodeBrix.Sqlite` - i.e. `using CodeBrix.Sqlite;`

XML documentation (IntelliSense) ships alongside the assembly.

## CodeBrix.Sqlite supports:

* Opening SQLite databases with sensible modern defaults — WAL journaling and enforced foreign keys — via the `SqliteDatabase` entry-point class (sync and async APIs throughout)
* Encrypting individual column values with any crypt engine implementing `IObjectCryptEngine`; a production-ready `AesGcmCryptEngine` (AES-GCM, random nonce per value, PBKDF2 key derivation) is included
* Storing and retrieving whole CLR objects in encrypted columns: `AddEncryptedParameter()`, `ExecuteDecrypt<T>()`, `GetDecrypted<T>()`, `TryDecrypt<T>()`
* The `EncryptedTable<T>` typed table abstraction: attribute-driven schema (`[NotEncrypted]`, `[Searchable]`, `[BlindIndexed]`, `[ColumnName]`, `[NotNull]`, `[ColumnDefaultValue]`), a TTL-cached searchable index over encrypted data, and a write-behind item cache
* HMAC-SHA256 blind-index columns for equality searches over encrypted values — indexed by SQLite itself, with no decrypt scan
* Safe backup orchestration: quiesce (maintenance mode) → WAL checkpoint → SQLite online backup → resume, plus a one-statement `VACUUM INTO` snapshot path
* Database maintenance mode, blocking normal operations while backups or schema changes run
* `user_version` schema-version helpers for managing database DDL upgrades over time
* Dapper-style CRUD extension methods on `SqliteConnection` — `Query<T>()`, `QueryFirst/Single(OrDefault)()`, `Execute()`, `ExecuteScalar<T>()`, `ExecuteReader()`, `QueryMultiple()` and their async forms, with anonymous-object parameters and IN-list expansion — that are encryption-aware: `EncryptedTableItem` results decrypt automatically, `[EncryptedColumn]` POCO properties decrypt on read, and `EncryptedValue`-wrapped parameters encrypt on bind
* Column binding that is case-insensitive **and underscore-tolerant**, so a `snake_case` schema maps onto PascalCase properties (`customer_tier` → `CustomerTier`) with no aliases, attributes or configuration to remember
* A SQLite dependency graph with no known security advisories — see below

## Every feature is optional — including encryption

The encryption features are what make this library different, but none of them are mandatory. The `cryptEngine` constructor argument is optional; omit it and CodeBrix.Sqlite is simply a convenience layer over `Microsoft.Data.Sqlite` — sensible pragmas, a Dapper-style mapper, and backup orchestration. You can adopt it for the plain case in two minutes and discover the encryption features later, without rewriting anything you wrote first.

## What the encryption does and does not cover

This is **selective column and object encryption on top of a normal SQLite file - it is not SQLCipher and not full-database (page-level) encryption**. The database file, its schema, its table and column names, and every column you did not encrypt remain readable by any SQLite tool. Encrypt the values that need protecting, and treat the file itself as unprotected.

Two further limits worth knowing before you design around it:

* Encrypted columns are opaque to SQLite, so there are no range, ordering or `LIKE` queries over them in SQL. Searching happens either through the in-memory searchable index (which decrypts a projection of every row) or through blind-index **exact equality**.
* Key management is out of scope: key derivation is PBKDF2/HKDF as described above, and there is no key storage, rotation or escrow.

## A clean SQLite dependency graph

CodeBrix.Sqlite pins the current 3.x `SQLitePCLRaw` native bundle — deliberately, not incidentally — rather than accepting whichever bundle `Microsoft.Data.Sqlite` would bring in transitively. Referencing this package therefore resolves a SQLite dependency graph that `dotnet list package --vulnerable --include-transitive` reports as clean, and your project needs no transitive pin of its own.

## Sample Code

### The plain case: no encryption at all

```csharp
using CodeBrix.Sqlite;

using var db = new SqliteDatabase("app.db");
db.SafeOpen(); // creates the file if missing; opens only if not already open
db.ExecuteNonQuery(
    "CREATE TABLE IF NOT EXISTS tickets (id INTEGER PRIMARY KEY, title TEXT, customer_tier TEXT);");

// The Dapper-style methods are extension methods on SqliteConnection,
// so they are reached through the Connection property:
db.Connection.Execute(
    "INSERT INTO tickets (title, customer_tier) VALUES (@Title, @CustomerTier);",
    new { Title = "Investigate timeout", CustomerTier = "gold" });

// 'customer_tier' binds to 'CustomerTier' with no alias and no attribute:
List<Ticket> tickets = db.Connection
    .Query<Ticket>("SELECT id, title, customer_tier FROM tickets ORDER BY id")
    .ToList();

public class Ticket
{
    public long Id { get; set; }
    public string Title { get; set; }
    public string CustomerTier { get; set; }
}
```

### Encrypting column values and backing up a live database

```csharp
using CodeBrix.Sqlite;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Extensions;

using var cryptEngine = new AesGcmCryptEngine("my secret passphrase");
using var database = new SqliteDatabase("/data/mydatabase.sqlite", cryptEngine);
database.Open(); // WAL mode + foreign keys enabled by default

database.ExecuteNonQuery(
    "CREATE TABLE IF NOT EXISTS [Notes] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Secret ENCRYPTED);");

using (var command = database.CreateCommand("INSERT INTO [Notes] (Secret) VALUES (@secret);"))
{
    command.AddEncryptedParameter("@secret", "This text is encrypted at rest.", cryptEngine);
    long rowId = command.ExecuteReturnRowId();
}

using (var command = database.CreateCommand("SELECT [Secret] FROM [Notes] LIMIT 1;"))
{
    string decrypted = command.ExecuteDecrypt<string>(cryptEngine);
}

// Safe backup: quiesce -> WAL checkpoint -> online backup -> resume
database.BackupToFile("/backups/mydatabase-backup.sqlite");
```

### A typed encrypted table with blind-index search

```csharp
using CodeBrix.Sqlite;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;

public class Contact : EncryptedTableItem
{
    [NotEncrypted] public string Category { get; set; }
    [Searchable] public string FullName { get; set; }
    [Searchable, BlindIndexed] public string Email { get; set; }
    public string PrivateNotes { get; set; } // encrypted, not searchable
}

using var cryptEngine = new AesGcmCryptEngine("my secret passphrase");
using var database = new SqliteDatabase("/data/mydatabase.sqlite", cryptEngine);

using (var contacts = new EncryptedTable<Contact>(database))
{
    contacts.AddItem(new Contact { FullName = "Ada Lovelace", Email = "ada@example.com" });
    contacts.WriteItemChanges();

    // Equality search via the HMAC blind index -- no decrypt scan:
    List<Contact> found = contacts.FindByBlindIndex(nameof(Contact.Email), "ada@example.com");
}
```

### Dapper-style queries that understand encryption

```csharp
using CodeBrix.Sqlite; // the mapper extension methods live in this namespace

// The connection of a SqliteDatabase knows its crypt engine ambiently:
var contacts = database.Connection
    .Query<Contact>("SELECT * FROM [Contact] WHERE [Category] = @cat;", new { cat = "Friends" })
    .ToList(); // each row's Encrypted_Object column is decrypted for you

// Encrypted parameter values and encrypted POCO columns:
database.Connection.Execute(
    "INSERT INTO [Vault] (Label, Secret) VALUES (@label, @secret);",
    new { label = "api-key", secret = new EncryptedValue("hunter2") });

public class VaultRow
{
    public long Id { get; set; }
    public string Label { get; set; }
    [EncryptedColumn] public string Secret { get; set; } // decrypted on read
}
var row = database.Connection.QuerySingle<VaultRow>("SELECT * FROM [Vault] WHERE [Label] = 'api-key';");
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.Sqlite.Tests` project:
https://github.com/ellisnet/CodeBrix.Sqlite/tree/main/tests/CodeBrix.Sqlite.Tests

## License

CodeBrix.Sqlite is licensed under the Apache License 2.0 - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Sqlite/blob/main/THIRD-PARTY-NOTICES.txt).
