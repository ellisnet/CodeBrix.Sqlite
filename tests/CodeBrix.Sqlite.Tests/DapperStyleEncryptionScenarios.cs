using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

//Scenario tests for the encryption-aware side of the Dapper-style SqliteMapper methods:
//  EncryptedValue parameters, [EncryptedColumn] POCO properties, EncryptedTableItem
//  materialization, ambient crypt-engine resolution, and the maintenance-mode gate.
public class DapperStyleEncryptionScenarios : IDisposable
{
    public class VaultRow
    {
        public long Id { get; set; }
        public string Label { get; set; }
        [EncryptedColumn] public string Secret { get; set; }
    }

    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("dapper-style scenario tests");
    private readonly SqliteDatabase _database;
    private readonly SqliteConnection _connection;

    public DapperStyleEncryptionScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("dapperstyle.sqlite"), _cryptEngine);
        _connection = _database.Connection;
        _connection.Execute("CREATE TABLE [Vault] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Label TEXT, Secret TEXT);");
        _connection.Execute(
            "INSERT INTO [Vault] (Label, Secret) VALUES (@label, @secret);",
            new { label = "first", secret = new EncryptedValue("the hidden text") });
        using (var table = new EncryptedTable<ContactItem>(_database))
        {
            table.AddItem(new ContactItem { Category = "Mapper", Age = 36, FullName = "Ada Lovelace", Email = "ada@example.com", PrivateNotes = "secret notes" });
            table.AddItem(new ContactItem { Category = "Mapper", Age = 85, FullName = "Grace Hopper", Email = "grace@example.com", PrivateNotes = "more notes" });
        }
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void EncryptedValue_parameter_stores_ciphertext_not_plaintext()
    {
        //Arrange + Act - read the raw column back without decryption
        string raw = _connection.ExecuteScalar<string>("SELECT [Secret] FROM [Vault] WHERE [Id] = 1;");

        //Assert
        raw.Should().NotBeNull();
        (raw == "the hidden text").Should().BeFalse();
        raw.Contains("hidden").Should().BeFalse();
    }

    [Fact]
    public void EncryptedColumn_property_is_decrypted_on_materialization()
    {
        //Arrange + Act
        VaultRow row = _connection.QuerySingle<VaultRow>("SELECT * FROM [Vault] WHERE [Id] = 1;");

        //Assert
        row.Label.Should().Be("first");
        row.Secret.Should().Be("the hidden text");
    }

    [Fact]
    public void EncryptedValue_of_null_stores_a_database_null()
    {
        //Arrange
        _connection.Execute(
            "INSERT INTO [Vault] (Label, Secret) VALUES ('nullrow', @secret);",
            new { secret = new EncryptedValue(null) });

        //Act
        VaultRow row = _connection.QuerySingle<VaultRow>("SELECT * FROM [Vault] WHERE [Label] = 'nullrow';");

        //Assert
        row.Secret.Should().BeNull();
    }

    [Fact]
    public void Query_materializes_encrypted_table_items_from_select_star()
    {
        //Arrange + Act - to a Dapper user this is just a query; behind the scenes each row's
        //  Encrypted_Object column is decrypted into a ContactItem
        List<ContactItem> contacts = _connection
            .Query<ContactItem>("SELECT * FROM [ContactItem] ORDER BY [Id];")
            .ToList();

        //Assert
        contacts.Count.Should().Be(2);
        contacts[0].FullName.Should().Be("Ada Lovelace");
        contacts[0].PrivateNotes.Should().Be("secret notes");
        (contacts[0].Id > 0).Should().BeTrue();
        contacts[0].SyncStatus.Should().Be(TableItemStatus.Unchanged);
    }

    [Fact]
    public void Query_filters_encrypted_table_items_on_plaintext_columns()
    {
        //Arrange + Act - [NotEncrypted] properties are real columns, so normal SQL WHERE works
        List<ContactItem> contacts = _connection
            .Query<ContactItem>("SELECT * FROM [ContactItem] WHERE [ContactAge] > @minAge;", new { minAge = 50 })
            .ToList();

        //Assert
        contacts.Count.Should().Be(1);
        contacts[0].FullName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void Query_finds_encrypted_table_items_via_the_blind_index_column()
    {
        //Arrange - equality search over encrypted data: hash the plaintext, compare in SQL
        string blindIndex = _cryptEngine.ComputeBlindIndex("ada@example.com");

        //Act
        List<ContactItem> contacts = _connection
            .Query<ContactItem>("SELECT * FROM [ContactItem] WHERE [BlindIndex_Email] = @ix;", new { ix = blindIndex })
            .ToList();

        //Assert
        contacts.Count.Should().Be(1);
        contacts[0].Email.Should().Be("ada@example.com");
    }

    [Fact]
    public void Query_of_encrypted_table_items_requires_the_encrypted_object_column()
    {
        //Arrange
        Action act = () => _connection.Query<ContactItem>("SELECT [Id] FROM [ContactItem];");

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Ambient_crypt_engine_is_resolved_from_the_owning_database()
    {
        //Arrange + Act - no cryptEngine argument anywhere: the connection belongs to a
        //  SqliteDatabase, so its engine is found ambiently
        ContactItem ada = _connection.QueryFirst<ContactItem>("SELECT * FROM [ContactItem] ORDER BY [Id];");

        //Assert
        ada.PrivateNotes.Should().Be("secret notes");
    }

    [Fact]
    public void Explicit_crypt_engine_works_on_a_bare_connection()
    {
        //Arrange - a second, plain Microsoft.Data.Sqlite connection with no SqliteDatabase around it
        using var bareConnection = new SqliteConnection($"Data Source={_folder.GetFilePath("dapperstyle.sqlite")};Pooling=False");

        //Act
        List<ContactItem> contacts = bareConnection
            .Query<ContactItem>("SELECT * FROM [ContactItem];", cryptEngine: _cryptEngine)
            .ToList();

        //Assert
        contacts.Count.Should().Be(2);
    }

    [Fact]
    public void Bare_connection_without_engine_throws_for_encrypted_table_items()
    {
        //Arrange
        using var bareConnection = new SqliteConnection($"Data Source={_folder.GetFilePath("dapperstyle.sqlite")};Pooling=False");
        Action act = () => bareConnection.Query<ContactItem>("SELECT * FROM [ContactItem];");

        //Act + Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void Bare_connection_without_engine_throws_for_encrypted_value_parameters()
    {
        //Arrange
        using var bareConnection = new SqliteConnection($"Data Source={_folder.GetFilePath("dapperstyle.sqlite")};Pooling=False");
        Action act = () => bareConnection.Execute(
            "INSERT INTO [Vault] (Secret) VALUES (@secret);", new { secret = new EncryptedValue("x") });

        //Act + Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void Maintenance_mode_blocks_mapper_operations()
    {
        //Arrange
        _database.BeginMaintenanceMode();
        Action act = () => _connection.Query<VaultRow>("SELECT * FROM [Vault];");

        //Act + Assert
        act.Should().Throw<DatabaseMaintenanceException>();
        _database.EndMaintenanceMode();
    }

    [Fact]
    public async Task QueryAsync_materializes_encrypted_table_items()
    {
        //Arrange + Act
        List<ContactItem> contacts = (await _connection.QueryAsync<ContactItem>(
            "SELECT * FROM [ContactItem] ORDER BY [Id];",
            cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        contacts.Count.Should().Be(2);
        contacts[1].PrivateNotes.Should().Be("more notes");
    }

    [Fact]
    public async Task ExecuteAsync_encrypts_EncryptedValue_parameters()
    {
        //Arrange
        await _connection.ExecuteAsync(
            "INSERT INTO [Vault] (Label, Secret) VALUES ('async', @secret);",
            new { secret = new EncryptedValue(12345) },
            cancellationToken: TestContext.Current.CancellationToken);

        //Act
        VaultRow row = await _connection.QuerySingleAsync<VaultRow>(
            "SELECT [Id], [Label] FROM [Vault] WHERE [Label] = 'async';",
            cancellationToken: TestContext.Current.CancellationToken);
        string raw = await _connection.ExecuteScalarAsync<string>(
            "SELECT [Secret] FROM [Vault] WHERE [Label] = 'async';",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert - VaultRow.Secret is a string property, so decrypt-to-string of an int value
        //  is not expected here; the raw value must simply be opaque ciphertext
        raw.Contains("12345").Should().BeFalse();
        row.Label.Should().Be("async");
    }

    [Fact]
    public void EncryptedColumn_with_the_wrong_engine_throws()
    {
        //Arrange - decrypting an [EncryptedColumn] property with a mismatched key must surface
        //  as ObjectCryptographyException, not a reflection wrapper exception
        using var wrongEngine = new AesGcmCryptEngine("the wrong passphrase");
        Action act = () => _connection.Query<VaultRow>(
            "SELECT * FROM [Vault] WHERE [Id] = 1;", cryptEngine: wrongEngine);

        //Act + Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void EncryptedColumn_round_trips_a_complex_object()
    {
        //Arrange - the encrypted column can hold any serializable object, not just strings
        var poco = new AesGcmCryptEngineTests.RoundTripPoco { Name = "boxed", Number = 3 };
        _connection.Execute(
            "INSERT INTO [Vault] (Label, Secret) VALUES ('complex', @secret);",
            new { secret = new EncryptedValue(poco) });

        //Act
        string encrypted = _connection.ExecuteScalar<string>("SELECT [Secret] FROM [Vault] WHERE [Label] = 'complex';");
        AesGcmCryptEngineTests.RoundTripPoco decrypted =
            _cryptEngine.DecryptObject<AesGcmCryptEngineTests.RoundTripPoco>(encrypted);

        //Assert
        decrypted.Name.Should().Be("boxed");
        decrypted.Number.Should().Be(3);
    }
}
