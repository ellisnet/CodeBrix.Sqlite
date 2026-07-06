# CodeBrix.Sqlite

A fully managed, cross-platform SQLite convenience library for .NET, layered on top of `Microsoft.Data.Sqlite`. CodeBrix.Sqlite provides selective column and object encryption with a pluggable crypt engine (including a ready-to-use AES-GCM engine), the typed `EncryptedTable<T>` abstraction with searchable encrypted data and HMAC blind-index equality search, safe quiesce-and-backup orchestration for live databases, and database schema-version helpers.
CodeBrix.Sqlite has a single dependency — `Microsoft.Data.Sqlite` — and is provided as a .NET 10 library and associated `CodeBrix.Sqlite.ApacheLicenseForever` NuGet package.

CodeBrix.Sqlite supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## CodeBrix.Sqlite supports:

* Opening SQLite databases with sensible modern defaults — WAL journaling and enforced foreign keys — via the `SqliteDatabase` entry-point class (sync and async APIs throughout)
* Encrypting individual column values with any crypt engine implementing `IObjectCryptEngine`; a production-ready `AesGcmCryptEngine` (AES-GCM, random nonce per value, PBKDF2 key derivation) is included
* Storing and retrieving whole CLR objects in encrypted columns: `AddEncryptedParameter()`, `ExecuteDecrypt<T>()`, `GetDecrypted<T>()`, `TryDecrypt<T>()`
* The `EncryptedTable<T>` typed table abstraction: attribute-driven schema (`[NotEncrypted]`, `[Searchable]`, `[BlindIndexed]`, `[ColumnName]`, `[NotNull]`, `[ColumnDefaultValue]`), a TTL-cached searchable index over encrypted data, and a write-behind item cache
* HMAC-SHA256 blind-index columns for equality searches over encrypted values — indexed by SQLite itself, with no decrypt scan
* Safe backup orchestration: quiesce (maintenance mode) → WAL checkpoint → SQLite online backup → resume, plus a one-statement `VACUUM INTO` snapshot path
* Database maintenance mode, blocking normal operations while backups or schema changes run
* `user_version` schema-version helpers for managing database DDL upgrades over time
* Dapper-style CRUD extension methods on `SqliteConnection` — `Query<T>()`, `QueryFirst/Single(OrDefault)()`, `Execute()`, `ExecuteScalar<T>()`, `ExecuteReader()`, `QueryMultiple()` and their async forms, with anonymous-object parameters and IN-list expansion (API modeled on Dapper 2.1.79) — that are encryption-aware: `EncryptedTableItem` results decrypt automatically, `[EncryptedColumn]` POCO properties decrypt on read, and `EncryptedValue`-wrapped parameters encrypt on bind

## Sample Code

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
using CodeBrix.Sqlite; // instead of 'using Dapper;'

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

## License

The project is licensed under the Apache 2.0 License. see: https://en.wikipedia.org/wiki/Apache_License
