using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;
using CodeBrix.Sqlite.Exceptions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

//Scenario tests that exercise TableSearch, TableIndex and EncryptedTable<T> search behavior together.
public class EncryptedTableSearchScenarios : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly AesGcmCryptEngine _cryptEngine = new AesGcmCryptEngine("search scenario tests");
    private readonly SqliteDatabase _database;
    private readonly EncryptedTable<ContactItem> _table;

    public EncryptedTableSearchScenarios()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("search.sqlite"), _cryptEngine);
        _table = new EncryptedTable<ContactItem>(_database);
        _table.AddItem(new ContactItem { Category = "Pioneers", Age = 36, FullName = "Ada Lovelace", Email = "ada@example.com", PrivateNotes = "analytical engine" });
        _table.AddItem(new ContactItem { Category = "Pioneers", Age = 85, FullName = "Grace Hopper", Email = "grace@example.com", PrivateNotes = "compilers" });
        _table.AddItem(new ContactItem { Category = "Modern", Age = 50, FullName = "Anita Borg", Email = "anita@example.com", PrivateNotes = "systers" });
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
    public void GetItems_matches_equality_on_searchable_property()
    {
        //Arrange
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "Ada Lovelace"));

        //Act
        List<ContactItem> items = _table.GetItems(search);

        //Assert
        items.Count.Should().Be(1);
        items[0].Email.Should().Be("ada@example.com");
    }

    [Fact]
    public void GetItems_matches_on_not_encrypted_property()
    {
        //Arrange - Category is [NotEncrypted]; plaintext columns join the searchable index too
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.Category), "Pioneers"));

        //Act
        List<ContactItem> items = _table.GetItems(search);

        //Assert
        items.Count.Should().Be(2);
    }

    [Fact]
    public void GetItems_is_case_insensitive_by_default()
    {
        //Arrange
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "ada lovelace"));

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(1);
    }

    [Fact]
    public void GetItems_honors_case_sensitivity()
    {
        //Arrange
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "ada lovelace"))
        {
            CaseSensitive = true
        };

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(0);
    }

    [Fact]
    public void GetItems_trims_values_by_default()
    {
        //Arrange
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "  Ada Lovelace  "));

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(1);
    }

    [Fact]
    public void GetItems_supports_contains_matching()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.FullName), "Hopper", SearchItemMatchType.Contains));

        //Act
        List<ContactItem> items = _table.GetItems(search);

        //Assert
        items.Count.Should().Be(1);
        items[0].FullName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void GetItems_supports_starts_with_matching()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.FullName), "A", SearchItemMatchType.StartsWith));

        //Act + Assert - Ada Lovelace and Anita Borg
        _table.GetItems(search).Count.Should().Be(2);
    }

    [Fact]
    public void GetItems_supports_ends_with_matching()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.FullName), "Borg", SearchItemMatchType.EndsWith));

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(1);
    }

    [Fact]
    public void GetItems_supports_is_not_equal_to_matching()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.Category), "Pioneers", SearchItemMatchType.IsNotEqualTo));

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(1);
    }

    [Fact]
    public void GetItems_match_all_requires_every_criterion()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.Category), "Pioneers"),
            new TableSearchItem(nameof(ContactItem.FullName), "Grace", SearchItemMatchType.StartsWith));

        //Act
        List<ContactItem> items = _table.GetItems(search);

        //Assert
        items.Count.Should().Be(1);
        items[0].FullName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void GetItems_match_any_accepts_any_criterion()
    {
        //Arrange
        var search = new TableSearch(
            new TableSearchItem(nameof(ContactItem.FullName), "Ada Lovelace"),
            new TableSearchItem(nameof(ContactItem.FullName), "Anita Borg"))
        {
            SearchType = TableSearchType.MatchAny
        };

        //Act + Assert
        _table.GetItems(search).Count.Should().Be(2);
    }

    [Fact]
    public void GetItems_with_empty_match_all_search_returns_everything()
        => _table.GetItems(new TableSearch()).Count.Should().Be(3);

    [Fact]
    public void GetItems_rejects_search_on_non_searchable_property()
    {
        //Arrange - PrivateNotes is encrypted and not [Searchable]
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.PrivateNotes), "compilers"));
        Action act = () => _table.GetItems(search);

        //Act + Assert
        act.Should().Throw<EncryptedTableException>();
    }

    [Fact]
    public async Task GetItemsAsync_matches_like_the_sync_version()
    {
        //Arrange
        var search = new TableSearch(new TableSearchItem(nameof(ContactItem.Category), "Modern"));

        //Act
        List<ContactItem> items = await _table.GetItemsAsync(
            search, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        items.Count.Should().Be(1);
        items[0].FullName.Should().Be("Anita Borg");
    }

    [Fact]
    public void GetItems_sees_pending_items_via_write_changes_first()
    {
        //Arrange
        _table.AddItem(new ContactItem { Category = "Modern", Age = 30, FullName = "New Person", Email = "new@example.com" });

        //Act - writeChangesFirst defaults to true
        List<ContactItem> items = _table.GetItems(
            new TableSearch(new TableSearchItem(nameof(ContactItem.FullName), "New Person")));

        //Assert
        items.Count.Should().Be(1);
    }

    [Fact]
    public void BuildFullTableIndex_reports_row_count()
        => _table.BuildFullTableIndex().Should().Be(3);

    [Fact]
    public async Task BuildFullTableIndexAsync_reports_row_count()
        => (await _table.BuildFullTableIndexAsync(TestContext.Current.CancellationToken)).Should().Be(3);

    [Fact]
    public void CheckFullTableIndex_is_false_before_any_build()
        => _table.CheckFullTableIndex().Should().BeFalse();

    [Fact]
    public void CheckFullTableIndex_is_true_after_build()
    {
        //Arrange
        _table.BuildFullTableIndex();

        //Act + Assert
        _table.CheckFullTableIndex().Should().BeTrue();
    }

    [Fact]
    public void CheckFullTableIndex_expires_with_zero_lifetime()
    {
        //Arrange
        _table.IndexLifetimeSeconds = 0;
        _table.BuildFullTableIndex();

        //Act + Assert - a zero-lifetime index is immediately expired
        _table.CheckFullTableIndex().Should().BeFalse();
    }

    [Fact]
    public void DropFullTableIndex_discards_the_index()
    {
        //Arrange
        _table.BuildFullTableIndex();

        //Act
        _table.DropFullTableIndex();

        //Assert
        _table.CheckFullTableIndex().Should().BeFalse();
    }

    [Fact]
    public void FullTableIndex_returns_a_clone_with_searchable_values()
    {
        //Arrange + Act
        TableIndex index = _table.FullTableIndex;

        //Assert
        index.Items.Count.Should().Be(3);
        bool foundAda = false;
        foreach (Dictionary<string, string> values in index.Items.Values)
        {
            if (values[nameof(ContactItem.FullName)] == "Ada Lovelace") { foundAda = true; }
        }
        foundAda.Should().BeTrue();
    }

    [Fact]
    public void WriteItemChanges_invalidates_the_index()
    {
        //Arrange
        _table.BuildFullTableIndex();
        _table.AddItem(new ContactItem { Category = "Modern", Age = 1, FullName = "Index Invalidator", Email = "x@example.com" });

        //Act
        _table.WriteItemChanges();

        //Assert
        _table.CheckFullTableIndex().Should().BeFalse();
    }
}
