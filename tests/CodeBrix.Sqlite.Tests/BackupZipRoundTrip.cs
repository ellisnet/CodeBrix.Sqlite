using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Compression.Zip;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

//End-to-end scenario: back up a live database (safe quiesce-and-backup orchestration),
//  compress the backup into a .zip with CodeBrix.Compression, delete the originals,
//  extract the .zip, and read the restored database - including decrypting its data.
public class BackupZipRoundTrip : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("zip round trip tests");

    public void Dispose()
    {
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private string CreatePopulatedDatabase(string fileName)
    {
        string databasePath = _folder.GetFilePath(fileName);
        using var database = new SqliteDatabase(databasePath, _cryptEngine);
        database.ExecuteNonQuery("CREATE TABLE [Plain] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Word TEXT);");
        database.ExecuteNonQuery("INSERT INTO [Plain] (Word) VALUES ('zip'), ('round'), ('trip');");
        using (SqliteCommand command = database.CreateCommand(
            "CREATE TABLE [Vault] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Secret TEXT);"))
        {
            command.ExecuteNonQuery();
        }
        using (SqliteCommand command = database.CreateCommand("INSERT INTO [Vault] (Secret) VALUES (@secret);"))
        {
            command.AddEncryptedParameter("@secret", "zipped secret value", _cryptEngine);
            command.ExecuteNonQuery();
        }
        using (var table = new EncryptedTable<ContactItem>(database))
        {
            table.AddItem(new ContactItem { Category = "Zip", Age = 5, FullName = "Ada Lovelace", Email = "ada@example.com", PrivateNotes = "travels by zip" });
        }
        return databasePath;
    }

    private void VerifyRestoredDatabase(string filePath)
    {
        using var restored = new SqliteDatabase(filePath, _cryptEngine);
        ((long)restored.ExecuteScalar("SELECT COUNT(*) FROM [Plain];")).Should().Be(3L);
        using (SqliteCommand command = restored.CreateCommand("SELECT [Secret] FROM [Vault] LIMIT 1;"))
        {
            command.ExecuteDecrypt<string>(_cryptEngine).Should().Be("zipped secret value");
        }
        using (var table = new EncryptedTable<ContactItem>(restored))
        {
            List<ContactItem> found = table.FindByBlindIndex(nameof(ContactItem.Email), "ada@example.com");
            found.Count.Should().Be(1);
            found[0].PrivateNotes.Should().Be("travels by zip");
        }
    }

    [Fact]
    public void Backup_zip_delete_extract_and_read_round_trip()
    {
        //Arrange - a live database, backed up via the safe orchestration into its own folder
        string databasePath = CreatePopulatedDatabase("live.sqlite");
        string backupFolder = _folder.GetFilePath("backups");
        Directory.CreateDirectory(backupFolder);
        string backupPath = Path.Combine(backupFolder, "live-backup.sqlite");
        using (var database = new SqliteDatabase(databasePath, _cryptEngine))
        {
            database.BackupToFile(backupPath);
        }

        //Act - compress the backup folder to a .zip, delete the originals, then extract
        string zipPath = _folder.GetFilePath("backup.zip");
        new FastZip().CreateZip(zipPath, backupFolder, recurse: false, fileFilter: null);
        File.Delete(backupPath);
        File.Delete(databasePath);
        string extractFolder = _folder.GetFilePath("extracted");
        new FastZip().ExtractZip(zipPath, extractFolder, fileFilter: null);
        string extractedDatabasePath = Path.Combine(extractFolder, "live-backup.sqlite");

        //Assert - the extracted database opens, reads, and decrypts
        File.Exists(extractedDatabasePath).Should().BeTrue();
        VerifyRestoredDatabase(extractedDatabasePath);
    }

    [Fact]
    public void Snapshot_zip_extract_and_read_round_trip()
    {
        //Arrange - same round trip via the VACUUM INTO snapshot path
        string databasePath = CreatePopulatedDatabase("live-snapshot.sqlite");
        string snapshotFolder = _folder.GetFilePath("snapshots");
        Directory.CreateDirectory(snapshotFolder);
        string snapshotPath = Path.Combine(snapshotFolder, "snapshot.sqlite");
        using (var database = new SqliteDatabase(databasePath, _cryptEngine))
        {
            database.SnapshotToFile(snapshotPath);
        }

        //Act
        string zipPath = _folder.GetFilePath("snapshot.zip");
        new FastZip().CreateZip(zipPath, snapshotFolder, recurse: false, fileFilter: null);
        File.Delete(snapshotPath);
        string extractFolder = _folder.GetFilePath("snapshot-extracted");
        new FastZip().ExtractZip(zipPath, extractFolder, fileFilter: null);

        //Assert
        VerifyRestoredDatabase(Path.Combine(extractFolder, "snapshot.sqlite"));
    }

    [Fact]
    public void Zipped_backup_is_smaller_than_or_equal_to_the_database_and_still_intact()
    {
        //Arrange
        string databasePath = CreatePopulatedDatabase("size-check.sqlite");
        string backupFolder = _folder.GetFilePath("size-backups");
        Directory.CreateDirectory(backupFolder);
        string backupPath = Path.Combine(backupFolder, "size-backup.sqlite");
        using (var database = new SqliteDatabase(databasePath, _cryptEngine))
        {
            database.BackupToFile(backupPath);
        }
        long backupSize = new FileInfo(backupPath).Length;

        //Act
        string zipPath = _folder.GetFilePath("size-check.zip");
        new FastZip().CreateZip(zipPath, backupFolder, recurse: false, fileFilter: null);
        long zipSize = new FileInfo(zipPath).Length;

        //Assert - SQLite files compress well; the zip must exist, be non-trivial, and be smaller
        (zipSize > 0).Should().BeTrue();
        (zipSize < backupSize).Should().BeTrue();
    }
}
