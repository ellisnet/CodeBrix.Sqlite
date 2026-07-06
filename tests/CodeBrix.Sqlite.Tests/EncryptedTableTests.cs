using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Exceptions;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class EncryptedTableTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("encrypted table tests");
    private readonly SqliteDatabase _database;

    public EncryptedTableTests()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("table.sqlite"), _cryptEngine);
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private static ContactItem CreateContact(string name = "Ada Lovelace", string email = "ada@example.com")
        => new ContactItem
        {
            Category = "Test",
            Age = 36,
            FullName = name,
            Email = email,
            PrivateNotes = "wrote the first program"
        };

    private List<string> GetTableColumnNames()
    {
        var names = new List<string>();
        using SqliteCommand command = _database.CreateCommand("PRAGMA table_info([ContactItem]);");
        _database.SafeOpen();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) { names.Add(reader.GetString(1)); }
        return names;
    }

    [Fact]
    public void Constructor_creates_the_expected_table_schema()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);

        //Act
        List<string> columns = GetTableColumnNames();

        //Assert - Id, plaintext columns (with [ColumnName] honored), blind index, and the two encrypted columns
        columns.Contains("Id").Should().BeTrue();
        columns.Contains("Category").Should().BeTrue();
        columns.Contains("ContactAge").Should().BeTrue();
        columns.Contains("BlindIndex_Email").Should().BeTrue();
        columns.Contains("Encrypted_Searchable").Should().BeTrue();
        columns.Contains("Encrypted_Object").Should().BeTrue();
        //Encrypted-only properties must NOT get their own columns:
        columns.Contains("FullName").Should().BeFalse();
        columns.Contains("Email").Should().BeFalse();
        columns.Contains("PrivateNotes").Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_null_database()
    {
        //Arrange
        Action act = () => new EncryptedTable<ContactItem>((SqliteDatabase)null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_requires_a_crypt_engine()
    {
        //Arrange
        using var plainDatabase = new SqliteDatabase(_folder.GetFilePath("nocrypt.sqlite"));
        Action act = () => new EncryptedTable<ContactItem>(plainDatabase);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Constructor_rejects_invalid_table_name()
    {
        //Arrange
        Action act = () => new EncryptedTable<ContactItem>(_database, tableName: "1BadName");

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Constructor_rejects_blind_index_without_provider()
    {
        //Arrange - ContactItem has a [BlindIndexed] property, but PlainTextCryptEngine
        //  does not implement IBlindIndexProvider
        using var plainEngine = new PlainTextCryptEngine();
        Action act = () => new EncryptedTable<ContactItem>(plainEngine, _database);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Constructor_rejects_contradictory_item_attributes()
    {
        //Arrange - BadDualAttributeItem marks the same property [NotEncrypted] and [BlindIndexed]
        Action act = () => new EncryptedTable<BadDualAttributeItem>(_database);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void AddItem_assigns_a_temporary_negative_id()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        ContactItem item = CreateContact();

        //Act
        long tempId = table.AddItem(item);

        //Assert
        (tempId < 0).Should().BeTrue();
        item.SyncStatus.Should().Be(TableItemStatus.New);
        table.TempItems.Count.Should().Be(1);
    }

    [Fact]
    public void WriteItemChanges_assigns_real_row_ids()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        ContactItem item = CreateContact();
        table.AddItem(item);

        //Act
        int changes = table.WriteItemChanges();

        //Assert
        changes.Should().Be(1);
        (item.Id > 0).Should().BeTrue();
        item.SyncStatus.Should().Be(TableItemStatus.Unchanged);
    }

    [Fact]
    public void GetItem_round_trips_all_property_kinds()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        ContactItem original = CreateContact();
        long id = table.AddItem(original, immediateWriteToTable: true);
        table.TempItems.Clear();

        //Act
        ContactItem loaded = table.GetItem(id);

        //Assert
        loaded.Category.Should().Be(original.Category);
        loaded.Age.Should().Be(original.Age);
        loaded.FullName.Should().Be(original.FullName);
        loaded.Email.Should().Be(original.Email);
        loaded.PrivateNotes.Should().Be(original.PrivateNotes);
        loaded.SyncStatus.Should().Be(TableItemStatus.Unchanged);
    }

    [Fact]
    public async Task GetItemAsync_round_trips_an_item()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        table.AddItem(CreateContact());
        int changes = await table.WriteItemChangesAsync(TestContext.Current.CancellationToken);
        long id = table.TempItems[0].Id;
        table.TempItems.Clear();

        //Act
        ContactItem loaded = await table.GetItemAsync(id, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        changes.Should().Be(1);
        loaded.FullName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void GetItem_returns_null_for_missing_item()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);

        //Act + Assert
        table.GetItem(99999).Should().BeNull();
    }

    [Fact]
    public void GetItem_throws_for_missing_item_when_requested()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);

        //Act
        Action act = () => table.GetItem(99999, exceptionOnMissingItem: true);

        //Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Encrypted_object_column_does_not_reveal_private_data()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        long id = table.AddItem(CreateContact(), immediateWriteToTable: true);

        //Act - read the raw stored columns
        using SqliteCommand command = _database.CreateCommand(
            "SELECT [Encrypted_Object], [Encrypted_Searchable] FROM [ContactItem] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        string rawObject = reader.GetString(0);
        string rawSearchable = reader.GetString(1);

        //Assert
        rawObject.Contains("wrote the first program").Should().BeFalse();
        rawObject.Contains("Ada Lovelace").Should().BeFalse();
        rawSearchable.Contains("Ada Lovelace").Should().BeFalse();
    }

    [Fact]
    public void UpdateItem_persists_changed_values()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        ContactItem item = CreateContact();
        long id = table.AddItem(item, immediateWriteToTable: true);
        item.FullName = "Ada King, Countess of Lovelace";
        item.PrivateNotes = "updated notes";

        //Act
        table.UpdateItem(item);
        table.WriteItemChanges();
        table.TempItems.Clear();
        ContactItem reloaded = table.GetItem(id);

        //Assert
        reloaded.FullName.Should().Be("Ada King, Countess of Lovelace");
        reloaded.PrivateNotes.Should().Be("updated notes");
    }

    [Fact]
    public void UpdateItem_rejects_an_unwritten_untracked_item()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        Action act = () => table.UpdateItem(CreateContact());

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void RemoveItem_drops_an_unwritten_item_without_touching_the_table()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        long tempId = table.AddItem(CreateContact());

        //Act
        table.RemoveItem(tempId);
        int changes = table.WriteItemChanges();

        //Assert
        changes.Should().Be(0);
        table.TempItems.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveItem_deletes_a_written_item_from_the_table()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);
        long id = table.AddItem(CreateContact(), immediateWriteToTable: true);

        //Act
        table.RemoveItem(id, immediateWriteToTable: true);
        table.TempItems.Clear();

        //Assert
        table.GetItem(id).Should().BeNull();
    }

    [Fact]
    public void WriteChangesOnDispose_flushes_pending_items()
    {
        //Arrange
        long id;
        using (var table = new EncryptedTable<ContactItem>(_database))
        {
            table.AddItem(CreateContact("Grace Hopper", "grace@example.com"));
            id = 0; //real id is assigned during dispose
        }

        //Act
        using var verifyTable = new EncryptedTable<ContactItem>(_database);
        List<ContactItem> items = verifyTable.GetItems(
            new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "Grace Hopper")));

        //Assert
        items.Count.Should().Be(1);
        items[0].Email.Should().Be("grace@example.com");
        (id == 0).Should().BeTrue();
    }

    [Fact]
    public void TableColumns_reports_the_full_schema()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database);

        //Act
        Dictionary<string, TableColumn> columns = table.TableColumns;

        //Assert
        columns.ContainsKey("Id").Should().BeTrue();
        columns.ContainsKey("Category").Should().BeTrue();
        columns.ContainsKey("ContactAge").Should().BeTrue();
        columns.ContainsKey("BlindIndex_Email").Should().BeTrue();
        columns.ContainsKey("Encrypted_Searchable").Should().BeTrue();
        columns.ContainsKey("Encrypted_Object").Should().BeTrue();
        columns["ContactAge"].DataType.Should().Be("INTEGER");
        columns["ContactAge"].PropertyName.Should().Be("Age");
    }

    [Fact]
    public void TableName_defaults_to_the_item_type_name()
        => new EncryptedTable<ContactItem>(_database).TableName.Should().Be("ContactItem");

    [Fact]
    public void TableName_can_be_overridden()
    {
        //Arrange
        using var table = new EncryptedTable<ContactItem>(_database, tableName: "MyContacts");

        //Act
        long count = (long)_database.ExecuteScalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MyContacts';");

        //Assert
        table.TableName.Should().Be("MyContacts");
        count.Should().Be(1L);
    }
}
