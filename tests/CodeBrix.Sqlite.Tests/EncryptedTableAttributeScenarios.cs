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

//Scenario tests for the EncryptedTable<T> attribute/type combinations that ContactItem does not
//  exercise: [NotNull], [ColumnDefaultValue], REAL/BLOB/bool/enum/DateTime/decimal plaintext
//  columns, the write-behind guard rails, and the async update/delete paths.
public class EncryptedTableAttributeScenarios : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("attribute scenario tests");
    private readonly SqliteDatabase _database;

    public EncryptedTableAttributeScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("attributes.sqlite"), _cryptEngine);
    }

    public void Dispose()
    {
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    private static InventoryItem CreateItem(string sku = "SKU-001")
        => new InventoryItem
        {
            Category = "widgets",
            Weight = 2.5,
            Thumbnail = new byte[] { 1, 2, 3, 4 },
            IsAvailable = true,
            Status = InventoryStatus.InStock,
            AddedOn = new DateTime(2026, 7, 5, 8, 30, 0, DateTimeKind.Unspecified),
            Price = 19.99m,
            Sku = sku,
            HiddenNotes = "restock in august"
        };

    private Dictionary<string, (string Type, bool NotNull, string Default)> GetTableInfo(string tableName)
    {
        var columns = new Dictionary<string, (string, bool, string)>();
        _database.SafeOpen();
        using SqliteCommand command = _database.CreateCommand($"PRAGMA table_info([{tableName}]);");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns[reader.GetString(1)] =
                (reader.GetString(2), reader.GetInt64(3) == 1, reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        return columns;
    }

    [Fact]
    public void CheckDbTable_maps_every_property_type_to_the_right_column_type()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);

        //Act
        Dictionary<string, (string Type, bool NotNull, string Default)> columns = GetTableInfo("InventoryItem");

        //Assert
        columns["Category"].Type.Should().Be("TEXT");
        columns["Weight"].Type.Should().Be("REAL");
        columns["Thumbnail"].Type.Should().Be("BLOB");
        columns["IsAvailable"].Type.Should().Be("INTEGER");
        columns["Status"].Type.Should().Be("INTEGER");
        columns["AddedOn"].Type.Should().Be("TEXT");
        columns["Price"].Type.Should().Be("TEXT");
        columns.ContainsKey("Sku").Should().BeFalse(); //[Searchable] only - no plaintext column
    }

    [Fact]
    public void CheckDbTable_applies_not_null_and_default_value()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);

        //Act
        Dictionary<string, (string Type, bool NotNull, string Default)> columns = GetTableInfo("InventoryItem");

        //Assert
        columns["Category"].NotNull.Should().BeTrue();
        columns["Category"].Default.Should().Be("'general'");
        columns["Weight"].NotNull.Should().BeFalse();
        columns["Weight"].Default.Should().BeNull();
    }

    [Fact]
    public void Item_with_every_property_type_round_trips()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem original = CreateItem();
        long id = table.AddItem(original, immediateWriteToTable: true);
        table.TempItems.Clear();

        //Act
        InventoryItem loaded = table.GetItem(id);

        //Assert
        loaded.Category.Should().Be("widgets");
        loaded.Weight.Should().Be(2.5);
        loaded.Thumbnail.Length.Should().Be(4);
        loaded.Thumbnail[3].Should().Be((byte)4);
        loaded.IsAvailable.Should().BeTrue();
        loaded.Status.Should().Be(InventoryStatus.InStock);
        loaded.AddedOn.Should().Be(new DateTime(2026, 7, 5, 8, 30, 0));
        loaded.Price.Should().Be(19.99m);
        loaded.Sku.Should().Be("SKU-001");
        loaded.HiddenNotes.Should().Be("restock in august");
    }

    [Fact]
    public void Plaintext_columns_store_the_expected_raw_values()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        long id = table.AddItem(CreateItem(), immediateWriteToTable: true);

        //Act - read raw column values with plain SQL
        using SqliteCommand command = _database.CreateCommand(
            "SELECT [IsAvailable], [Status], [Weight] FROM [InventoryItem] WHERE [Id] = @id;");
        command.Parameters.AddWithValue("@id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();

        //Assert - bool as 1, enum as its numeric value, double as REAL
        reader.GetInt64(0).Should().Be(1L);
        reader.GetInt64(1).Should().Be((long)InventoryStatus.InStock);
        reader.GetDouble(2).Should().Be(2.5);
    }

    [Fact]
    public void GetItems_supports_does_not_contain_matching()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        table.AddItem(CreateItem("SKU-ALPHA"));
        table.AddItem(CreateItem("SKU-BETA"));
        table.AddItem(CreateItem("OTHER-1"));
        table.WriteItemChanges();

        //Act
        List<InventoryItem> items = table.GetItems(new TableSearch(
            new TableSearchItem(nameof(InventoryItem.Sku), "SKU-", SearchItemMatchType.DoesNotContain)));

        //Assert
        items.Count.Should().Be(1);
        items[0].Sku.Should().Be("OTHER-1");
    }

    [Fact]
    public void WriteChangesOnDispose_false_discards_pending_items()
    {
        //Arrange
        using (var table = new EncryptedTable<InventoryItem>(_database, tableName: "NoFlushTable"))
        {
            table.WriteChangesOnDispose = false;
            table.AddItem(CreateItem("SKU-DISCARDED"));
            table.WriteChangesOnDispose.Should().BeFalse();
        }

        //Act
        using var verifyTable = new EncryptedTable<InventoryItem>(_database, tableName: "NoFlushTable");
        int indexed = verifyTable.BuildFullTableIndex();

        //Assert - nothing was flushed on dispose
        indexed.Should().Be(0);
    }

    [Fact]
    public async Task WriteItemChangesAsync_updates_a_modified_item()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem item = CreateItem("SKU-ASYNC-UPDATE");
        table.AddItem(item);
        await table.WriteItemChangesAsync(TestContext.Current.CancellationToken);
        item.Price = 24.99m;
        item.Sku = "SKU-ASYNC-UPDATED";
        table.UpdateItem(item);

        //Act
        int changes = await table.WriteItemChangesAsync(TestContext.Current.CancellationToken);
        table.TempItems.Clear();
        InventoryItem reloaded = await table.GetItemAsync(item.Id, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        changes.Should().Be(1);
        reloaded.Price.Should().Be(24.99m);
        reloaded.Sku.Should().Be("SKU-ASYNC-UPDATED");
    }

    [Fact]
    public async Task WriteItemChangesAsync_deletes_a_pending_removal()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem item = CreateItem("SKU-ASYNC-DELETE");
        table.AddItem(item);
        await table.WriteItemChangesAsync(TestContext.Current.CancellationToken);
        table.RemoveItem(item.Id);

        //Act
        int changes = await table.WriteItemChangesAsync(TestContext.Current.CancellationToken);

        //Assert
        changes.Should().Be(1);
        (await table.GetItemAsync(item.Id, cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public void AddItem_rejects_an_item_that_was_already_written()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem item = CreateItem();
        table.AddItem(item, immediateWriteToTable: true);
        table.TempItems.Clear();

        //Act
        Action act = () => table.AddItem(item);

        //Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void AddItem_rejects_the_same_instance_twice()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem item = CreateItem();
        table.AddItem(item);

        //Act
        Action act = () => table.AddItem(item);

        //Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void RemoveItem_attaches_an_untracked_real_row_id_and_deletes_it()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        long id = table.AddItem(CreateItem(), immediateWriteToTable: true);
        table.TempItems.Clear(); //make the row untracked

        //Act
        table.RemoveItem(id, immediateWriteToTable: true);
        table.TempItems.Clear();

        //Assert
        table.GetItem(id).Should().BeNull();
    }

    [Fact]
    public void RemoveItem_rejects_an_untracked_temporary_id()
    {
        //Arrange
        using var table = new EncryptedTable<InventoryItem>(_database);
        Action act = () => table.RemoveItem(-42);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void WriteItemChanges_rejects_a_modified_item_with_a_temporary_id()
    {
        //Arrange - manually forcing Modified onto an unwritten item is a caller error
        using var table = new EncryptedTable<InventoryItem>(_database);
        InventoryItem item = CreateItem();
        table.AddItem(item);
        item.SyncStatus = TableItemStatus.Modified;

        //Act
        Action act = () => table.WriteItemChanges();

        //Assert
        act.Should().Throw<EncryptedTableException>();
        table.WriteChangesOnDispose = false; //the bad item would throw again from the dispose flush
    }

    [Fact]
    public void BuildFullTableIndex_with_the_wrong_engine_throws()
    {
        //Arrange - rows written with one engine, index built with another
        using (var table = new EncryptedTable<InventoryItem>(_database))
        {
            table.AddItem(CreateItem());
        }
        using var wrongEngine = new AesGcmCryptEngine("a different passphrase");
        using var wrongDatabase = new SqliteDatabase(_folder.GetFilePath("attributes.sqlite"), wrongEngine);
        using var wrongTable = new EncryptedTable<InventoryItem>(wrongDatabase);

        //Act
        Action act = () => wrongTable.BuildFullTableIndex();

        //Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void Explicit_engine_constructor_round_trips()
    {
        //Arrange - engine passed explicitly instead of via the database
        using var plainDatabase = new SqliteDatabase(_folder.GetFilePath("explicit.sqlite"));
        using var table = new EncryptedTable<InventoryItem>(_cryptEngine, plainDatabase);
        long id = table.AddItem(CreateItem("SKU-EXPLICIT"), immediateWriteToTable: true);
        table.TempItems.Clear();

        //Act
        InventoryItem loaded = table.GetItem(id);

        //Assert
        loaded.Sku.Should().Be("SKU-EXPLICIT");
    }

    [Fact]
    public void Constructor_rejects_reserved_column_name_collisions()
    {
        //Arrange - ReservedColumnItem maps a property to the reserved Encrypted_Object column
        Action act = () => new EncryptedTable<ReservedColumnItem>(_database);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void ColumnNameAttribute_rejects_invalid_names()
    {
        //Arrange
        Action act = () => new ColumnNameAttribute("1-bad name");

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void ColumnDefaultValueAttribute_stores_its_value()
        => new ColumnDefaultValueAttribute("general").Value.Should().Be("general");

    [Fact]
    public void ColumnDefaultValueAttribute_rejects_null()
    {
        //Arrange
        Action act = () => new ColumnDefaultValueAttribute(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ColumnDefaultValueAttribute_rejects_single_quotes()
    {
        //Arrange
        Action act = () => new ColumnDefaultValueAttribute("bad'value");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }
}
