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

//Scenario tests for the HMAC blind-index feature: schema, storage opacity, search behavior.
public class BlindIndexScenarios : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("blind index scenario tests");
    private readonly SqliteDatabase _database;
    private readonly EncryptedTable<ContactItem> _table;

    public BlindIndexScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("blindindex.sqlite"), _cryptEngine);
        _table = new EncryptedTable<ContactItem>(_database);
        _table.AddItem(new ContactItem { Category = "A", Age = 1, FullName = "Ada Lovelace", Email = "ada@example.com" });
        _table.AddItem(new ContactItem { Category = "B", Age = 2, FullName = "Grace Hopper", Email = "grace@example.com" });
        _table.AddItem(new ContactItem { Category = "C", Age = 3, FullName = "Second Ada", Email = "ada@example.com" });
        _table.WriteItemChanges();
        _table.TempItems.Clear();
    }

    public void Dispose()
    {
        _table.Dispose();
        _database.Dispose();
        _cryptEngine.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void CheckDbTable_creates_a_real_sqlite_index_for_the_blind_index_column()
    {
        //Arrange + Act
        long count = (long)_database.ExecuteScalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ContactItem_BlindIndex_Email';");

        //Assert
        count.Should().Be(1L);
    }

    [Fact]
    public void Stored_blind_index_value_is_the_hmac_not_the_plaintext()
    {
        //Arrange
        string expected = _cryptEngine.ComputeBlindIndex("ada@example.com");

        //Act
        using SqliteCommand command = _database.CreateCommand(
            "SELECT [BlindIndex_Email] FROM [ContactItem] WHERE [Id] = 1;");
        _database.SafeOpen();
        string stored = (string)command.ExecuteScalar();

        //Assert
        stored.Should().Be(expected);
        stored.Contains("ada@example.com").Should().BeFalse();
    }

    [Fact]
    public void FindByBlindIndex_finds_all_exact_matches()
    {
        //Arrange + Act
        List<ContactItem> items = _table.FindByBlindIndex(nameof(ContactItem.Email), "ada@example.com");

        //Assert - two contacts share the address
        items.Count.Should().Be(2);
    }

    [Fact]
    public void FindByBlindIndex_returns_empty_for_no_match()
        => _table.FindByBlindIndex(nameof(ContactItem.Email), "nobody@example.com").Count.Should().Be(0);

    [Fact]
    public void FindByBlindIndex_is_case_sensitive()
        => _table.FindByBlindIndex(nameof(ContactItem.Email), "ADA@EXAMPLE.COM").Count.Should().Be(0);

    [Fact]
    public async Task FindByBlindIndexAsync_finds_matches()
    {
        //Arrange + Act
        List<ContactItem> items = await _table.FindByBlindIndexAsync(
            nameof(ContactItem.Email), "grace@example.com", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        items.Count.Should().Be(1);
        items[0].FullName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void FindByBlindIndex_rejects_a_property_without_the_attribute()
    {
        //Arrange - FullName is [Searchable] but not [BlindIndexed]
        Action act = () => _table.FindByBlindIndex(nameof(ContactItem.FullName), "Ada Lovelace");

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public void FindByBlindIndex_rejects_null_value()
    {
        //Arrange
        Action act = () => _table.FindByBlindIndex(nameof(ContactItem.Email), null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FindByBlindIndex_sees_pending_items_via_write_changes_first()
    {
        //Arrange
        _table.AddItem(new ContactItem { Category = "D", Age = 4, FullName = "Pending Person", Email = "pending@example.com" });

        //Act - writeChangesFirst defaults to true
        List<ContactItem> items = _table.FindByBlindIndex(nameof(ContactItem.Email), "pending@example.com");

        //Assert
        items.Count.Should().Be(1);
    }

    [Fact]
    public void Updating_an_item_updates_its_blind_index()
    {
        //Arrange
        List<ContactItem> found = _table.FindByBlindIndex(nameof(ContactItem.Email), "grace@example.com");
        ContactItem grace = found[0];
        grace.Email = "hopper@example.com";

        //Act
        _table.UpdateItem(grace);
        _table.WriteItemChanges();

        //Assert
        _table.FindByBlindIndex(nameof(ContactItem.Email), "grace@example.com").Count.Should().Be(0);
        _table.FindByBlindIndex(nameof(ContactItem.Email), "hopper@example.com").Count.Should().Be(1);
    }
}
