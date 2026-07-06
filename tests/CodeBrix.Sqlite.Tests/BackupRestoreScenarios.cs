using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

//Scenario tests for the safe quiesce-and-backup orchestration and the VACUUM INTO snapshot path:
//  every backup is verified by opening it as a fresh database and reading (and decrypting) the data.
public class BackupRestoreScenarios : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("backup scenario tests");
    private readonly SqliteDatabase _database;

    public BackupRestoreScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("source.sqlite"), _cryptEngine);
        //A plain table...
        _database.ExecuteNonQuery("CREATE TABLE [Plain] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Word TEXT);");
        _database.ExecuteNonQuery("INSERT INTO [Plain] (Word) VALUES ('alpha'), ('beta'), ('gamma');");
        //...an encrypted-column value...
        _database.ExecuteNonQuery("CREATE TABLE [Vault] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Secret TEXT);");
        using (SqliteCommand command = _database.CreateCommand("INSERT INTO [Vault] (Secret) VALUES (@secret);"))
        {
            command.AddEncryptedParameter("@secret", "backup this secret", _cryptEngine);
            command.ExecuteNonQuery();
        }
        //...and an encrypted table with a blind index.
        using (var table = new EncryptedTable<ContactItem>(_database))
        {
            table.AddItem(new ContactItem { Category = "Backup", Age = 1, FullName = "Ada Lovelace", Email = "ada@example.com", PrivateNotes = "notes" });
        }
        _database.SetSchemaVersion(42);
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private void VerifyDatabaseContents(string filePath)
    {
        using var restored = new SqliteDatabase(filePath, _cryptEngine);
        //Plain data survived:
        ((long)restored.ExecuteScalar("SELECT COUNT(*) FROM [Plain];")).Should().Be(3L);
        ((string)restored.ExecuteScalar("SELECT [Word] FROM [Plain] WHERE [Id] = 2;")).Should().Be("beta");
        //Encrypted column value survived and still decrypts:
        using (SqliteCommand command = restored.CreateCommand("SELECT [Secret] FROM [Vault] LIMIT 1;"))
        {
            command.ExecuteDecrypt<string>(_cryptEngine).Should().Be("backup this secret");
        }
        //Encrypted table survived, decrypts, and its blind index still works:
        using (var table = new EncryptedTable<ContactItem>(restored))
        {
            List<ContactItem> found = table.FindByBlindIndex(nameof(ContactItem.Email), "ada@example.com");
            found.Count.Should().Be(1);
            found[0].PrivateNotes.Should().Be("notes");
        }
        //Schema version survived:
        restored.GetSchemaVersion().Should().Be(42L);
    }

    [Fact]
    public void BackupToFile_produces_a_readable_backup()
    {
        //Arrange
        string backupPath = _folder.GetFilePath("backup.sqlite");

        //Act
        _database.BackupToFile(backupPath);

        //Assert
        File.Exists(backupPath).Should().BeTrue();
        VerifyDatabaseContents(backupPath);
    }

    [Fact]
    public async Task BackupToFileAsync_produces_a_readable_backup()
    {
        //Arrange
        string backupPath = _folder.GetFilePath("backup-async.sqlite");

        //Act
        await _database.BackupToFileAsync(backupPath, TestContext.Current.CancellationToken);

        //Assert
        File.Exists(backupPath).Should().BeTrue();
        VerifyDatabaseContents(backupPath);
    }

    [Fact]
    public void BackupToFile_leaves_the_source_out_of_maintenance_mode()
    {
        //Arrange
        string backupPath = _folder.GetFilePath("backup-mode.sqlite");

        //Act
        _database.BackupToFile(backupPath);

        //Assert - the quiesce is released, and normal operations work again
        _database.IsInMaintenanceMode.Should().BeFalse();
        ((long)_database.ExecuteScalar("SELECT COUNT(*) FROM [Plain];")).Should().Be(3L);
    }

    [Fact]
    public void BackupToFile_overwrites_an_existing_backup()
    {
        //Arrange
        string backupPath = _folder.GetFilePath("backup-overwrite.sqlite");
        _database.BackupToFile(backupPath);
        _database.ExecuteNonQuery("INSERT INTO [Plain] (Word) VALUES ('delta');");

        //Act
        _database.BackupToFile(backupPath);

        //Assert
        using var restored = new SqliteDatabase(backupPath, _cryptEngine);
        ((long)restored.ExecuteScalar("SELECT COUNT(*) FROM [Plain];")).Should().Be(4L);
    }

    [Fact]
    public void BackupToFile_captures_unCheckpointed_wal_data()
    {
        //Arrange - fresh writes sit in the WAL until a checkpoint; the orchestration
        //  checkpoints before the page copy, so they must appear in the backup
        _database.ExecuteNonQuery("INSERT INTO [Plain] (Word) VALUES ('wal-resident');");
        string backupPath = _folder.GetFilePath("backup-wal.sqlite");

        //Act
        _database.BackupToFile(backupPath);

        //Assert
        using var restored = new SqliteDatabase(backupPath, _cryptEngine);
        ((long)restored.ExecuteScalar(
            "SELECT COUNT(*) FROM [Plain] WHERE [Word] = 'wal-resident';")).Should().Be(1L);
    }

    [Fact]
    public void SnapshotToFile_produces_a_readable_snapshot()
    {
        //Arrange
        string snapshotPath = _folder.GetFilePath("snapshot.sqlite");

        //Act
        _database.SnapshotToFile(snapshotPath);

        //Assert
        File.Exists(snapshotPath).Should().BeTrue();
        VerifyDatabaseContents(snapshotPath);
    }

    [Fact]
    public async Task SnapshotToFileAsync_produces_a_readable_snapshot()
    {
        //Arrange
        string snapshotPath = _folder.GetFilePath("snapshot-async.sqlite");

        //Act
        await _database.SnapshotToFileAsync(snapshotPath, TestContext.Current.CancellationToken);

        //Assert
        VerifyDatabaseContents(snapshotPath);
    }

    [Fact]
    public void SnapshotToFile_refuses_an_existing_destination()
    {
        //Arrange
        string snapshotPath = _folder.GetFilePath("snapshot-existing.sqlite");
        File.WriteAllText(snapshotPath, "already here");

        //Act
        Action act = () => _database.SnapshotToFile(snapshotPath);

        //Assert
        act.Should().Throw<IOException>();
    }

    [Fact]
    public async Task SnapshotToFileAsync_refuses_an_existing_destination()
    {
        //Arrange
        string snapshotPath = _folder.GetFilePath("snapshot-async-existing.sqlite");
        File.WriteAllText(snapshotPath, "already here");

        //Act + Assert
        await Assert.ThrowsAsync<IOException>(
            () => _database.SnapshotToFileAsync(snapshotPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BackupToFile_rejects_null_destination()
    {
        //Arrange
        Action act = () => _database.BackupToFile(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
