using System;
using CodeBrix.Sqlite.EncryptedTables;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class TableSearchTests
{
    [Fact]
    public void defaults_are_match_all_case_insensitive_trimmed()
    {
        //Arrange + Act
        var search = new TableSearch();

        //Assert
        search.SearchType.Should().Be(TableSearchType.MatchAll);
        search.CaseSensitive.Should().BeFalse();
        search.TrimValues.Should().BeTrue();
        search.MatchItems.Count.Should().Be(0);
    }

    [Fact]
    public void params_constructor_stores_the_criteria()
    {
        //Arrange + Act
        var search = new TableSearch(
            new TableSearchItem("A", "1"),
            new TableSearchItem("B", "2"));

        //Assert
        search.MatchItems.Count.Should().Be(2);
        search.MatchItems[1].PropertyName.Should().Be("B");
    }

    [Fact]
    public void params_constructor_rejects_null()
    {
        //Arrange
        Action act = () => new TableSearch((TableSearchItem[])null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
