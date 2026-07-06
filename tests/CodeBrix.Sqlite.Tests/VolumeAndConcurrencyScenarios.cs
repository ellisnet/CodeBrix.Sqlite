using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

//Hardening scenarios: moderate row volumes, and multiple connections on one WAL database
//  (concurrent reader, backup while a reader holds the file open).
public class VolumeAndConcurrencyScenarios : IDisposable
{
    private const int RowCount = 250;

    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("volume scenario tests");
    private readonly SqliteDatabase _database;

    public VolumeAndConcurrencyScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("volume.sqlite"), _cryptEngine);
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private void SeedContacts(EncryptedTable<ContactItem> table)
    {
        for (int i = 0; i < RowCount; i++)
        {
            table.AddItem(new ContactItem
            {
                Category = (i % 2 == 0) ? "Even" : "Odd",
                Age = i,
                FullName = $"Person Number {i:D4}",
                Email = $"person{i}@example.com",
                PrivateNotes = $"notes for person {i}"
            });
        }
        table.WriteItemChanges();
        table.TempItems.Clear();
    }

    [Fact]
    public void Encrypted_table_handles_a_few_hundred_rows()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        SeedContacts(table);

        //Act
        int indexed = table.BuildFullTableIndex();
        List<ContactItem> evens = table.GetItems(new TableSearch(
            new TableSearchItem(nameof(ContactItem.Category), "Even")));
        List<ContactItem> byBlindIndex = table.FindByBlindIndex(nameof(ContactItem.Email), "person123@example.com");

        //Assert
        indexed.Should().Be(RowCount);
        evens.Count.Should().Be(RowCount / 2);
        byBlindIndex.Count.Should().Be(1);
        byBlindIndex[0].FullName.Should().Be("Person Number 0123");
    }

    [Fact]
    public void Backup_of_a_larger_database_round_trips_all_rows()
    {
        //Arrange
        using (var table = new EncryptedTable<ContactItem>(_database))
        {
            SeedContacts(table);
        }
        string backupPath = _folder.GetFilePath("volume-backup.sqlite");

        //Act
        _database.BackupToFile(backupPath);

        //Assert
        using var restored = new SqliteDatabase(backupPath, _cryptEngine);
        ((long)restored.ExecuteScalar("SELECT COUNT(*) FROM [ContactItem];")).Should().Be((long)RowCount);
        using var restoredTable = new EncryptedTable<ContactItem>(restored);
        restoredTable.FindByBlindIndex(nameof(ContactItem.Email), "person200@example.com").Count.Should().Be(1);
    }

    [Fact]
    public void Second_connection_sees_committed_writes_under_wal()
    {
        //Arrange
        _database.ExecuteNonQuery("CREATE TABLE [Log] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Entry TEXT);");
        _database.ExecuteNonQuery("INSERT INTO [Log] (Entry) VALUES ('first');");
        using var secondConnection = new SqliteConnection($"Data Source={_database.DatabaseFilePath};Pooling=False");

        //Act - interleave writes on the primary connection with reads on the second
        long firstCount = secondConnection.ExecuteScalar<long>("SELECT COUNT(*) FROM [Log];");
        _database.ExecuteNonQuery("INSERT INTO [Log] (Entry) VALUES ('second');");
        long secondCount = secondConnection.ExecuteScalar<long>("SELECT COUNT(*) FROM [Log];");

        //Assert
        firstCount.Should().Be(1L);
        secondCount.Should().Be(2L);
    }

    [Fact]
    public void Backup_succeeds_while_another_connection_holds_an_open_reader()
    {
        //Arrange
        _database.ExecuteNonQuery("CREATE TABLE [Busy] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Word TEXT);");
        _database.ExecuteNonQuery("INSERT INTO [Busy] (Word) VALUES ('a'), ('b'), ('c');");
        using var readerConnection = new SqliteConnection($"Data Source={_database.DatabaseFilePath};Pooling=False");
        readerConnection.Open();
        string backupPath = _folder.GetFilePath("busy-backup.sqlite");

        //Act - keep a reader mid-result-set on the second connection during the backup
        using (SqliteDataReader reader = readerConnection.ExecuteReader("SELECT * FROM [Busy];"))
        {
            reader.Read(); //positioned on the first row, result set still open
            _database.BackupToFile(backupPath);
        }

        //Assert
        using var restored = new SqliteDatabase(backupPath, _cryptEngine);
        ((long)restored.ExecuteScalar("SELECT COUNT(*) FROM [Busy];")).Should().Be(3L);
        _database.IsInMaintenanceMode.Should().BeFalse();
    }

    [Fact]
    public void Custom_serializer_is_used_by_the_crypt_engine()
    {
        //Arrange
        var spy = new SpySerializer();
        using var engine = new AesGcmCryptEngine("spy serializer test", serializer: spy);

        //Act
        string decrypted = engine.DecryptObject<string>(engine.EncryptObject("watch me"));

        //Assert
        decrypted.Should().Be("watch me");
        (spy.SerializeCount > 0).Should().BeTrue();
        (spy.DeserializeCount > 0).Should().BeTrue();
    }

    [Fact]
    public void Custom_serializer_is_exposed_by_the_database_options()
    {
        //Arrange
        var spy = new SpySerializer();

        //Act
        using var database = new SqliteDatabase(
            _folder.GetFilePath("serializer.sqlite"), options: new SqliteDatabaseOptions { Serializer = spy });

        //Assert
        database.Serializer.Should().Be(spy);
    }
}
