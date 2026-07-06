using System;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Enumerations;
using CodeBrix.Sqlite.Exceptions;
using CodeBrix.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class SqliteDataReaderExtensionsTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("reader extension tests");
    private readonly SqliteDatabase _database;

    public SqliteDataReaderExtensionsTests()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("readers.sqlite"), _cryptEngine);
        _database.ExecuteNonQuery(
            "CREATE TABLE [Rows] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Encrypted TEXT);");
        using (SqliteCommand command = _database.CreateCommand(
            "INSERT INTO [Rows] (Encrypted) VALUES (@one), (@two), (NULL), (@bad);"))
        {
            command.AddEncryptedParameter("@one", "first value", _cryptEngine);
            command.AddEncryptedParameter("@two", 12345, _cryptEngine);
            command.Parameters.AddWithValue("@bad", "this is not valid ciphertext");
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private SqliteDataReader OpenReaderAtRow(long id)
    {
        SqliteCommand command = _database.CreateCommand("SELECT [Id], [Encrypted] FROM [Rows] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", id);
        SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return reader;
    }

    [Fact]
    public void GetDecrypted_by_column_name_round_trips()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(1);

        //Act
        string decrypted = reader.GetDecrypted<string>("Encrypted", _cryptEngine);

        //Assert
        decrypted.Should().Be("first value");
    }

    [Fact]
    public void GetDecrypted_by_ordinal_round_trips()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(2);

        //Act
        int decrypted = reader.GetDecrypted<int>(1, _cryptEngine);

        //Assert
        decrypted.Should().Be(12345);
    }

    [Fact]
    public void GetDecrypted_throws_on_null_column_by_default()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(3);

        //Act
        Action act = () => reader.GetDecrypted<string>("Encrypted", _cryptEngine);

        //Assert
        act.Should().Throw<DbNullValueException>();
    }

    [Fact]
    public void GetDecrypted_returns_default_on_null_column_when_requested()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(3);

        //Act
        string decrypted = reader.GetDecrypted<string>("Encrypted", _cryptEngine, DbNullHandling.ReturnTypeDefaultValue);

        //Assert
        decrypted.Should().BeNull();
    }

    [Fact]
    public void TryDecrypt_returns_true_and_the_value_on_success()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(1);

        //Act
        bool success = reader.TryDecrypt("Encrypted", _cryptEngine, out string value);

        //Assert
        success.Should().BeTrue();
        value.Should().Be("first value");
    }

    [Fact]
    public void TryDecrypt_returns_false_on_null_column()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(3);

        //Act
        bool success = reader.TryDecrypt("Encrypted", _cryptEngine, out string value);

        //Assert
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryDecrypt_returns_false_on_undecryptable_data()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(4);

        //Act
        bool success = reader.TryDecrypt("Encrypted", _cryptEngine, out string value);

        //Assert
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void GetDecrypted_rejects_null_reader()
    {
        //Arrange
        Action act = () => ((SqliteDataReader)null).GetDecrypted<string>(0, _cryptEngine);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDecrypted_rejects_null_column_name()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(1);
        Action act = () => reader.GetDecrypted<string>((string)null, _cryptEngine);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDecrypted_rejects_null_crypt_engine()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(1);
        Action act = () => reader.GetDecrypted<string>(1, null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryDecrypt_rejects_null_reader()
    {
        //Arrange
        Action act = () => ((SqliteDataReader)null).TryDecrypt(0, _cryptEngine, out string _);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryDecrypt_by_ordinal_round_trips()
    {
        //Arrange
        using SqliteDataReader reader = OpenReaderAtRow(1);

        //Act
        bool success = reader.TryDecrypt(1, _cryptEngine, out string value);

        //Assert
        success.Should().BeTrue();
        value.Should().Be("first value");
    }
}
