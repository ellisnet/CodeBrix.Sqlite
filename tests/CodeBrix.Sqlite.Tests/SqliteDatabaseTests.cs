using System;
using System.Data;
using System.IO;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class SqliteDatabaseTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();

    public void Dispose() => _folder.Dispose();

    private SqliteDatabase CreateDatabase(string fileName = "test.sqlite", SqliteDatabaseOptions options = null)
        => new SqliteDatabase(_folder.GetFilePath(fileName), options: options);

    [Fact]
    public void Constructor_rejects_null_path()
    {
        //Arrange
        Action act = () => new SqliteDatabase(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_whitespace_path()
    {
        //Arrange
        Action act = () => new SqliteDatabase("  ");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_creates_the_database_file()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        database.Open();

        //Assert
        File.Exists(database.DatabaseFilePath).Should().BeTrue();
        database.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async System.Threading.Tasks.Task OpenAsync_creates_the_database_file()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        await database.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        File.Exists(database.DatabaseFilePath).Should().BeTrue();
        database.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public void Open_uses_wal_journal_mode_by_default()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();

        //Act
        string journalMode = (string)database.ExecuteScalar("PRAGMA journal_mode;");

        //Assert
        journalMode.Should().Be("wal");
    }

    [Fact]
    public void Open_honors_disabled_write_ahead_logging()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase(
            "nowal.sqlite", new SqliteDatabaseOptions { UseWriteAheadLogging = false });
        database.Open();

        //Act
        string journalMode = (string)database.ExecuteScalar("PRAGMA journal_mode;");

        //Assert
        journalMode.Should().Be("delete");
    }

    [Fact]
    public void Open_enforces_foreign_keys_by_default()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();

        //Act
        long foreignKeys = (long)database.ExecuteScalar("PRAGMA foreign_keys;");

        //Assert
        foreignKeys.Should().Be(1L);
    }

    [Fact]
    public void Constructor_with_CreateIfMissing_false_fails_on_missing_file()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase(
            "missing.sqlite", new SqliteDatabaseOptions { CreateIfMissing = false });

        //Act
        Action act = () => database.Open();

        //Assert
        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void SafeOpen_does_not_throw_when_already_open()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();

        //Act
        database.SafeOpen();

        //Assert
        database.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public void ExecuteNonQuery_creates_a_table()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        database.ExecuteNonQuery("CREATE TABLE [Words] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Word TEXT);");
        long count = (long)database.ExecuteScalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Words';");

        //Assert
        count.Should().Be(1L);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteNonQueryAsync_inserts_rows()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        await database.ExecuteNonQueryAsync(
            "CREATE TABLE [Words] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Word TEXT);",
            cancellationToken: TestContext.Current.CancellationToken);

        //Act
        int affected = await database.ExecuteNonQueryAsync(
            "INSERT INTO [Words] (Word) VALUES ('hello');",
            cancellationToken: TestContext.Current.CancellationToken);
        object count = await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM [Words];",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        affected.Should().Be(1);
        ((long)count).Should().Be(1L);
    }

    [Fact]
    public void ExecuteNonQuery_rejects_null_sql()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        Action act = () => database.ExecuteNonQuery(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetSchemaVersion_defaults_to_zero()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act + Assert
        database.GetSchemaVersion().Should().Be(0L);
    }

    [Fact]
    public void SetSchemaVersion_round_trips()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        database.SetSchemaVersion(7);

        //Assert
        database.GetSchemaVersion().Should().Be(7L);
        database.IsInMaintenanceMode.Should().BeFalse();
    }

    [Fact]
    public async System.Threading.Tasks.Task SetSchemaVersionAsync_round_trips()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();

        //Act
        await database.SetSchemaVersionAsync(11, TestContext.Current.CancellationToken);

        //Assert
        (await database.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().Be(11L);
        database.IsInMaintenanceMode.Should().BeFalse();
    }

    [Fact]
    public void GetSchemaVersion_leaves_a_closed_connection_closed()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.Close();

        //Act
        database.GetSchemaVersion();

        //Assert
        database.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void GetSchemaVersion_leaves_an_open_connection_open()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();

        //Act
        database.GetSchemaVersion();

        //Assert
        database.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSchemaVersionAsync_leaves_a_closed_connection_closed()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.Close();

        //Act
        await database.GetSchemaVersionAsync(TestContext.Current.CancellationToken);

        //Assert
        database.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void BeginMaintenanceMode_blocks_normal_operations()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.BeginMaintenanceMode();

        //Act
        Action act = () => database.ExecuteNonQuery("CREATE TABLE [Blocked] (Id INTEGER);");

        //Assert
        act.Should().Throw<DatabaseMaintenanceException>();
    }

    [Fact]
    public void BeginMaintenanceMode_allows_maintenance_operations()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.BeginMaintenanceMode();

        //Act
        database.ExecuteNonQuery("CREATE TABLE [Allowed] (Id INTEGER);", forMaintenance: true);
        database.EndMaintenanceMode();

        //Assert
        long count = (long)database.ExecuteScalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Allowed';");
        count.Should().Be(1L);
    }

    [Fact]
    public void Maintenance_operations_require_maintenance_mode()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();

        //Act
        Action act = () => database.ExecuteNonQuery("CREATE TABLE [Nope] (Id INTEGER);", forMaintenance: true);

        //Assert
        act.Should().Throw<DatabaseMaintenanceException>();
    }

    [Fact]
    public void CreateCommand_honors_the_maintenance_gate()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.BeginMaintenanceMode();

        //Act
        Action act = () => database.CreateCommand("SELECT 1;");

        //Assert
        act.Should().Throw<DatabaseMaintenanceException>();
        database.EndMaintenanceMode();
    }

    [Fact]
    public void EndMaintenanceMode_restores_normal_operations()
    {
        //Arrange
        using SqliteDatabase database = CreateDatabase();
        database.Open();
        database.BeginMaintenanceMode();
        database.EndMaintenanceMode();

        //Act
        database.ExecuteNonQuery("CREATE TABLE [Restored] (Id INTEGER);");

        //Assert
        database.IsInMaintenanceMode.Should().BeFalse();
    }

    [Fact]
    public void Dispose_blocks_further_use()
    {
        //Arrange
        SqliteDatabase database = CreateDatabase();
        database.Open();
        database.Dispose();

        //Act
        Action act = () => database.ExecuteScalar("SELECT 1;");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}
