using System;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Enumerations;
using CodeBrix.Sqlite.Exceptions;
using CodeBrix.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class SqliteCommandExtensionsTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("command extension tests");
    private readonly SqliteDatabase _database;

    public SqliteCommandExtensionsTests()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("commands.sqlite"), _cryptEngine);
        _database.ExecuteNonQuery(
            "CREATE TABLE [Vault] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Label TEXT, Secret ENCRYPTED);");
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private long InsertEncrypted(object secret)
    {
        using SqliteCommand command = _database.CreateCommand(
            "INSERT INTO [Vault] (Label, Secret) VALUES (@label, @secret);");
        command.Parameters.AddWithValue("@label", "test row");
        command.AddEncryptedParameter("@secret", secret, _cryptEngine);
        return command.ExecuteReturnRowId();
    }

    [Fact]
    public void AddEncryptedParameter_stores_an_opaque_value()
    {
        //Arrange
        const string secret = "the plaintext secret";
        long rowId = InsertEncrypted(secret);

        //Act - read the raw stored column value
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        string raw = (string)command.ExecuteScalar();

        //Assert
        (raw == secret).Should().BeFalse();
        raw.Contains(secret).Should().BeFalse();
    }

    [Fact]
    public void ExecuteDecrypt_round_trips_the_value()
    {
        //Arrange
        const string secret = "decrypt me";
        long rowId = InsertEncrypted(secret);

        //Act
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        string decrypted = command.ExecuteDecrypt<string>(_cryptEngine);

        //Assert
        decrypted.Should().Be(secret);
    }

    [Fact]
    public async Task ExecuteDecryptAsync_round_trips_the_value()
    {
        //Arrange
        const string secret = "decrypt me asynchronously";
        long rowId = InsertEncrypted(secret);

        //Act
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        string decrypted = await command.ExecuteDecryptAsync<string>(
            _cryptEngine, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        decrypted.Should().Be(secret);
    }

    [Fact]
    public void ExecuteDecrypt_round_trips_a_complex_object()
    {
        //Arrange
        var secret = new AesGcmCryptEngineTests.RoundTripPoco { Name = "nested", Number = 9 };
        long rowId = InsertEncrypted(secret);

        //Act
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        AesGcmCryptEngineTests.RoundTripPoco decrypted =
            command.ExecuteDecrypt<AesGcmCryptEngineTests.RoundTripPoco>(_cryptEngine);

        //Assert
        decrypted.Name.Should().Be("nested");
        decrypted.Number.Should().Be(9);
    }

    [Fact]
    public void ExecuteDecrypt_throws_on_null_column_by_default()
    {
        //Arrange
        long rowId = InsertEncrypted(null);

        //Act
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        Action act = () => command.ExecuteDecrypt<string>(_cryptEngine);

        //Assert
        act.Should().Throw<DbNullValueException>();
    }

    [Fact]
    public void ExecuteDecrypt_returns_default_on_null_column_when_requested()
    {
        //Arrange
        long rowId = InsertEncrypted(null);

        //Act
        using SqliteCommand command = _database.CreateCommand("SELECT [Secret] FROM [Vault] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", rowId);
        string decrypted = command.ExecuteDecrypt<string>(_cryptEngine, DbNullHandling.ReturnTypeDefaultValue);

        //Assert
        decrypted.Should().BeNull();
    }

    [Fact]
    public void ExecuteReturnRowId_returns_increasing_row_ids()
    {
        //Arrange + Act
        long first = InsertEncrypted("one");
        long second = InsertEncrypted("two");

        //Assert
        (second > first).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteReturnRowIdAsync_returns_the_new_row_id()
    {
        //Arrange
        using SqliteCommand command = _database.CreateCommand(
            "INSERT INTO [Vault] (Label, Secret) VALUES ('async row', NULL);");

        //Act
        long rowId = await command.ExecuteReturnRowIdAsync(TestContext.Current.CancellationToken);

        //Assert
        (rowId > 0).Should().BeTrue();
    }

    [Fact]
    public void AddEncryptedParameter_rejects_null_command()
    {
        //Arrange
        Action act = () => ((SqliteCommand)null).AddEncryptedParameter("@p", "x", _cryptEngine);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddEncryptedParameter_rejects_whitespace_parameter_name()
    {
        //Arrange
        using SqliteCommand command = _database.CreateCommand("SELECT 1;");
        Action act = () => command.AddEncryptedParameter("   ", "x", _cryptEngine);

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExecuteDecrypt_rejects_null_command()
    {
        //Arrange
        Action act = () => ((SqliteCommand)null).ExecuteDecrypt<string>(_cryptEngine);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExecuteDecrypt_rejects_null_crypt_engine()
    {
        //Arrange
        using SqliteCommand command = _database.CreateCommand("SELECT 1;");
        Action act = () => command.ExecuteDecrypt<string>(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExecuteReturnRowId_rejects_null_command()
    {
        //Arrange
        Action act = () => ((SqliteCommand)null).ExecuteReturnRowId();

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExecuteReturnRowId_rejects_a_command_without_a_connection()
    {
        //Arrange
        using var command = new SqliteCommand("SELECT 1;");
        Action act = () => command.ExecuteReturnRowId();

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddEncryptedParameter_rejects_null_crypt_engine()
    {
        //Arrange
        using SqliteCommand command = _database.CreateCommand("SELECT 1;");
        Action act = () => command.AddEncryptedParameter("@p", "x", null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
